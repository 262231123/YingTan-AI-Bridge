# Dynamo CPython3/PythonNet3 node script for Revit 2027.
#
# Inputs:
#   IN[0] command input: JSON file path, JSON string, or Dynamo dictionary.
#   IN[1] optional result JSON file path.
#   IN[2] allow writes: boolean gate. Required for dryRun=false.
#
# Output:
#   OUT result dictionary.

import json
import os
import traceback

import clr
import System

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (  # noqa: E402
    BuiltInCategory,
    ElementId,
    FilteredElementCollector,
    Level,
    Line,
    StorageType,
    Wall,
    WallType,
    XYZ,
)

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager  # noqa: E402
from RevitServices.Transactions import TransactionManager  # noqa: E402


MM_PER_FOOT = 304.8


def element_id_value(element_id):
    return getattr(element_id, "Value", element_id.IntegerValue)


def mm_to_feet(value):
    return float(value) / MM_PER_FOOT


def feet_to_mm(value):
    return float(value) * MM_PER_FOOT


def point_from_mm(data):
    return XYZ(
        mm_to_feet(data.get("xMm", 0)),
        mm_to_feet(data.get("yMm", 0)),
        mm_to_feet(data.get("zMm", 0)),
    )


def point_to_mm(point):
    return {
        "xMm": feet_to_mm(point.X),
        "yMm": feet_to_mm(point.Y),
        "zMm": feet_to_mm(point.Z),
    }


def load_payload(command_input):
    if command_input is None:
        raise Exception("IN[0] must be a JSON file path, JSON string, or dictionary.")

    if isinstance(command_input, dict):
        return command_input

    raw = str(command_input)

    if os.path.exists(raw):
        with open(raw, "r", encoding="utf-8") as command_file:
            return json.load(command_file)

    return json.loads(raw)


def write_result(result_path, result):
    if not result_path:
        return

    folder = os.path.dirname(str(result_path))
    if folder and not os.path.exists(folder):
        os.makedirs(folder)

    with open(str(result_path), "w", encoding="utf-8") as result_file:
        json.dump(result, result_file, ensure_ascii=False, indent=2)


def get_current_context():
    uiapp = DocumentManager.Instance.CurrentUIApplication
    uidoc = uiapp.ActiveUIDocument
    doc = DocumentManager.Instance.CurrentDBDocument

    if doc is None:
        raise Exception("No active Revit document is open.")

    return uiapp, uidoc, doc


def require_write_allowed(payload, allow_writes):
    if not payload.get("dryRun", True) and not allow_writes:
        raise Exception("dryRun=false was requested, but IN[2] allow writes is not enabled.")


def run_transaction(doc, name, callback):
    TransactionManager.Instance.EnsureInTransaction(doc)

    try:
        result = callback()
        TransactionManager.Instance.TransactionTaskDone()
        return result
    except Exception:
        TransactionManager.Instance.ForceCloseTransaction()
        raise


def get_active_document(uiapp, uidoc, doc):
    active_view = uidoc.ActiveView.Name if uidoc and uidoc.ActiveView else None

    return {
        "hasDocument": True,
        "title": doc.Title,
        "path": doc.PathName,
        "isFamilyDocument": doc.IsFamilyDocument,
        "isWorkshared": doc.IsWorkshared,
        "activeView": active_view,
        "revitVersion": uiapp.Application.VersionNumber,
    }


def list_levels(doc):
    levels = list(FilteredElementCollector(doc).OfClass(Level))
    levels.sort(key=lambda level: level.Elevation)

    return {
        "count": len(levels),
        "levels": [
            {
                "id": element_id_value(level.Id),
                "name": level.Name,
                "elevationFeet": level.Elevation,
                "elevationMm": feet_to_mm(level.Elevation),
            }
            for level in levels
        ],
    }


def list_wall_types(doc):
    wall_types = list(FilteredElementCollector(doc).OfClass(WallType))
    wall_types.sort(key=lambda wall_type: wall_type.Name)

    return {
        "count": len(wall_types),
        "wallTypes": [
            {
                "id": element_id_value(wall_type.Id),
                "name": wall_type.Name,
                "familyName": wall_type.FamilyName,
                "kind": str(wall_type.Kind),
            }
            for wall_type in wall_types
        ],
    }


def count_elements(doc, payload):
    collector = FilteredElementCollector(doc).WhereElementIsNotElementType()
    category = payload.get("category")

    if category:
        built_in_category = System.Enum.Parse(BuiltInCategory, str(category))
        collector = collector.OfCategory(built_in_category)

    return {
        "category": category,
        "count": collector.GetElementCount(),
    }


def read_parameter_value(parameter):
    storage_type = parameter.StorageType

    if storage_type == StorageType.String:
        return parameter.AsString()

    if storage_type == StorageType.Integer:
        return parameter.AsInteger()

    if storage_type == StorageType.Double:
        return {
            "raw": parameter.AsDouble(),
            "display": parameter.AsValueString(),
        }

    if storage_type == StorageType.ElementId:
        return element_id_value(parameter.AsElementId())

    return parameter.AsValueString()


def write_parameter_value(parameter, value, double_unit):
    storage_type = parameter.StorageType

    if storage_type == StorageType.String:
        parameter.Set("" if value is None else str(value))
        return

    if storage_type == StorageType.Integer:
        if isinstance(value, bool):
            parameter.Set(1 if value else 0)
        else:
            parameter.Set(int(value))
        return

    if storage_type == StorageType.Double:
        numeric = float(value)
        if str(double_unit).lower() == "mm":
            numeric = mm_to_feet(numeric)
        parameter.Set(numeric)
        return

    if storage_type == StorageType.ElementId:
        parameter.Set(ElementId(int(value)))
        return

    raise Exception("Cannot write parameter storage type: {}".format(storage_type))


def set_parameter(doc, payload):
    dry_run = payload.get("dryRun", True)
    element_id = int(payload["elementId"])
    parameter_name = str(payload["parameterName"])
    value = payload.get("value")

    element = doc.GetElement(ElementId(element_id))
    if element is None:
        raise Exception("Element {} was not found.".format(element_id))

    parameter = element.LookupParameter(parameter_name)
    if parameter is None:
        raise Exception("Element {} does not have parameter '{}'.".format(element_id, parameter_name))

    if parameter.IsReadOnly:
        raise Exception("Parameter '{}' is read-only.".format(parameter_name))

    before = read_parameter_value(parameter)

    def do_write():
        write_parameter_value(parameter, value, payload.get("doubleUnit"))
        return read_parameter_value(parameter)

    after = before if dry_run else run_transaction(doc, "Codex Dynamo set parameter", do_write)

    return {
        "dryRun": dry_run,
        "elementId": element_id,
        "elementName": element.Name,
        "parameterName": parameter_name,
        "before": before,
        "proposed": value,
        "after": after,
    }


def resolve_level(doc, payload):
    if payload.get("levelId") is not None:
        level = doc.GetElement(ElementId(int(payload["levelId"])))
        if isinstance(level, Level):
            return level
        raise Exception("Level {} was not found.".format(payload["levelId"]))

    level_name = payload.get("levelName")
    levels = list(FilteredElementCollector(doc).OfClass(Level))

    if level_name:
        for level in levels:
            if level.Name.lower() == str(level_name).lower():
                return level
        raise Exception("Level '{}' was not found.".format(level_name))

    if not levels:
        raise Exception("The active document has no levels.")

    levels.sort(key=lambda level: level.Elevation)
    return levels[0]


def resolve_wall_type(doc, payload):
    if payload.get("wallTypeId") is not None:
        wall_type = doc.GetElement(ElementId(int(payload["wallTypeId"])))
        if isinstance(wall_type, WallType):
            return wall_type
        raise Exception("Wall type {} was not found.".format(payload["wallTypeId"]))

    wall_type_name = payload.get("wallTypeName")
    wall_types = list(FilteredElementCollector(doc).OfClass(WallType))

    if wall_type_name:
        for wall_type in wall_types:
            if wall_type.Name.lower() == str(wall_type_name).lower():
                return wall_type
        raise Exception("Wall type '{}' was not found.".format(wall_type_name))

    if not wall_types:
        raise Exception("The active document has no wall types.")

    return wall_types[0]


def create_wall(doc, payload):
    dry_run = payload.get("dryRun", True)
    level = resolve_level(doc, payload)
    wall_type = resolve_wall_type(doc, payload)
    start = point_from_mm(payload["start"])
    end = point_from_mm(payload["end"])
    height_mm = float(payload.get("heightMm", 3000))
    height_feet = mm_to_feet(height_mm)
    line = Line.CreateBound(start, end)

    def do_write():
        wall = Wall.Create(doc, line, wall_type.Id, level.Id, height_feet, 0, False, False)
        return element_id_value(wall.Id)

    created_element_id = None if dry_run else run_transaction(doc, "Codex Dynamo create wall", do_write)

    return {
        "dryRun": dry_run,
        "level": {
            "id": element_id_value(level.Id),
            "name": level.Name,
        },
        "wallType": {
            "id": element_id_value(wall_type.Id),
            "name": wall_type.Name,
        },
        "startMm": point_to_mm(start),
        "endMm": point_to_mm(end),
        "heightMm": height_mm,
        "createdElementId": created_element_id,
    }


def execute(payload, allow_writes):
    uiapp, uidoc, doc = get_current_context()
    command = str(payload.get("command", "")).lower()

    if command == "health":
        return {
            "bridge": "revit-codex-bridge-dynamo",
            "mode": "dynamo-player",
            "revitVersion": uiapp.Application.VersionNumber,
            "commands": [
                "health",
                "get_active_document",
                "list_levels",
                "list_wall_types",
                "count_elements",
                "set_parameter",
                "create_wall",
            ],
        }

    if command == "get_active_document":
        return get_active_document(uiapp, uidoc, doc)

    if command == "list_levels":
        return list_levels(doc)

    if command == "list_wall_types":
        return list_wall_types(doc)

    if command == "count_elements":
        return count_elements(doc, payload)

    if command == "set_parameter":
        require_write_allowed(payload, allow_writes)
        return set_parameter(doc, payload)

    if command == "create_wall":
        require_write_allowed(payload, allow_writes)
        return create_wall(doc, payload)

    raise Exception("Unsupported Dynamo bridge command '{}'.".format(command))


command_input = IN[0] if len(IN) > 0 else None
result_path = IN[1] if len(IN) > 1 else None
allow_writes = bool(IN[2]) if len(IN) > 2 and IN[2] is not None else False

try:
    payload = load_payload(command_input)
    result = {
        "ok": True,
        "result": execute(payload, allow_writes),
        "error": None,
    }
except Exception as ex:
    result = {
        "ok": False,
        "result": None,
        "error": str(ex),
        "trace": traceback.format_exc(),
    }

write_result(result_path, result)
OUT = result

