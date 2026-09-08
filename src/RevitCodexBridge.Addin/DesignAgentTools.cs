using System.Runtime.CompilerServices;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitCodexBridge.Addin;

internal static class DesignAgentTools
{
    private sealed class Identity { public string Token { get; } = Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<Document, Identity> Documents = new();
    public static string Token(Document document) => Documents.GetValue(document, _ => new Identity()).Token;

    public static void CheckDocument(UIApplication app, JsonElement payload)
    {
        var expected = payload.GetOptionalString("expectedDocumentToken");
        if (expected is not null && (app.ActiveUIDocument is null || Token(app.ActiveUIDocument.Document) != expected))
            throw new InvalidOperationException("当前文档已切换或重新打开，请重新发送需求生成计划。");
    }

    public static object Context(UIApplication app)
    {
        var ui = app.ActiveUIDocument ?? throw new InvalidOperationException("请先打开 Revit 项目。");
        var doc = ui.Document;
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x => x.Elevation).ToList();
        var types = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().ToList();
        var ids = ui.Selection.GetElementIds();
        return new
        {
            documentToken = Token(doc), title = doc.Title, revitVersion = app.Application.VersionNumber,
            doc.IsReadOnly, doc.IsFamilyDocument, doc.IsWorkshared,
            activeView = new { id = ui.ActiveView.Id.Value, name = ui.ActiveView.Name, levelId = ui.ActiveView.GenLevel?.Id.Value },
            selectedCount = ids.Count, selected = ids.Take(50).Select(doc.GetElement).Where(x => x is not null).Select(ElementInfo).ToArray(),
            levelCount = levels.Count, levels = levels.Take(50).Select(x => new { id = x.Id.Value, x.Name, elevationMm = x.Elevation * 304.8 }).ToArray(),
            wallTypeCount = types.Count, wallTypes = types.Take(50).Select(x => new { id = x.Id.Value, x.Name, kind = x.Kind.ToString(), widthMm = x.Width * 304.8 }).ToArray(),
            note = "摘要最多50项；不足请用查询命令补齐。坐标是项目内部坐标毫米。"
        };
    }

    public static object Find(UIApplication app, JsonElement payload)
    {
        var ui = app.ActiveUIDocument ?? throw new InvalidOperationException("请先打开项目。");
        var categoryText = payload.GetRequiredString("category");
        if (!Enum.TryParse<BuiltInCategory>(categoryText, out var category) || !Enum.IsDefined(category))
            throw new InvalidOperationException("未知类别：" + categoryText);
        var offset = (int)Math.Clamp(payload.GetOptionalDouble("offset", 0), 0, int.MaxValue);
        var limit = (int)Math.Clamp(payload.GetOptionalDouble("limit", 50), 1, 100);
        var filter = payload.GetOptionalString("nameContains");
        var selected = ui.Selection.GetElementIds().ToHashSet();
        var selectionOnly = payload.GetOptionalBoolean("selectedOnly", false);
        var matches = new FilteredElementCollector(ui.Document).OfCategory(category).WhereElementIsNotElementType()
            .Where(x => !selectionOnly || selected.Contains(x.Id))
            .Where(x => filter is null || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Where(x => !payload.TryGetProperty("levelId", out var level) || x.LevelId.Value == level.GetInt64())
            .OrderBy(x => x.Id.Value).ToList();
        return new { total = matches.Count, offset, limit, hasMore = offset + limit < matches.Count,
            elements = matches.Skip(offset).Take(limit).Select(ElementInfo).ToArray() };
    }

    public static object Warnings(Document doc, JsonElement payload)
    {
        var offset = (int)Math.Clamp(payload.GetOptionalDouble("offset", 0), 0, int.MaxValue);
        var limit = (int)Math.Clamp(payload.GetOptionalDouble("limit", 50), 1, 100);
        var warnings = doc.GetWarnings();
        return new { total = warnings.Count, offset, hasMore = offset + limit < warnings.Count,
            warnings = warnings.Skip(offset).Take(limit).Select(w => new { description = w.GetDescriptionText(),
                severity = w.GetSeverity().ToString(), elementIds = w.GetFailingElements().Select(x => x.Value).ToArray() }).ToArray() };
    }

    private static object ElementInfo(Element e)
    {
        var point = e.Location is LocationPoint lp ? lp.Point : e.Location is LocationCurve lc ? lc.Curve.Evaluate(0.5, true) : null;
        return new { id = e.Id.Value, uniqueId = e.UniqueId, name = e.Name, category = e.Category?.Name,
            typeId = e.GetTypeId().Value, levelId = e.LevelId.Value,
            positionMm = point is null ? null : new { x = point.X * 304.8, y = point.Y * 304.8, z = point.Z * 304.8 } };
    }

    public static object RoomLayout(Document doc, JsonElement p)
    {
        if (doc.IsReadOnly || doc.IsFamilyDocument) throw new InvalidOperationException("需要可写的项目文档。");
        var level = doc.GetElement(new ElementId(p.GetRequiredInt64("levelId"))) as Level
            ?? throw new InvalidOperationException("标高不存在。");
        var type = doc.GetElement(new ElementId(p.GetRequiredInt64("wallTypeId"))) as WallType;
        if (type is null || type.Kind != WallKind.Basic) throw new InvalidOperationException("请选择基本墙类型。");
        var width = Dimension(p, "widthMm", 500, 100000);
        var depth = Dimension(p, "depthMm", 500, 100000);
        var height = Dimension(p, "heightMm", 500, 20000);
        var x = Dimension(p, "originXmm", -10000000, 10000000);
        var y = Dimension(p, "originYmm", -10000000, 10000000);
        var name = p.GetRequiredString("name");
        if (width <= type.Width * 304.8 || depth <= type.Width * 304.8)
            throw new InvalidOperationException("宽深必须大于墙厚。");
        var corners = new[] { new XYZ(x, y, 0), new XYZ(x + width, y, 0), new XYZ(x + width, y + depth, 0), new XYZ(x, y + depth, 0) }
            .Select(q => new XYZ(q.X / 304.8, q.Y / 304.8, level.Elevation)).ToArray();
        var center = new UV((x + width / 2) / 304.8, (y + depth / 2) / 304.8);
        if (doc.GetRoomAtPoint(new XYZ(center.U, center.V, level.Elevation + 0.5)) is not null)
            throw new InvalidOperationException("目标中心已位于现有房间内，请选择空区域或修改现有房间。");
        if (p.GetOptionalBoolean("dryRun", true))
            return new { dryRun = true, name, widthMm = width, depthMm = depth, heightMm = height, wallsToCreate = 4,
                note = "墙中心线尺寸；将创建四面墙与一个房间，不含门窗和楼板。现有边界可能影响房间形状。" };
        if (p.GetOptionalBoolean("confirmInRevit", true) && TaskDialog.Show("创建矩形房间", $"创建 {name}：{width} × {depth} mm，4面墙及房间。", TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
            throw new OperationCanceledException("用户取消创建。");
        using var tx = new Transaction(doc, "AI 创建矩形房间");
        tx.Start();
        var walls = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            var wall = Wall.Create(doc, Line.CreateBound(corners[i], corners[(i + 1) % 4]), type!.Id, level.Id, height / 304.8, 0, false, false);
            wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING)?.Set(1);
            walls.Add(wall.Id.Value);
        }
        doc.Regenerate();
        var room = doc.Create.NewRoom(level, center);
        room.Name = name;
        var number = p.GetOptionalString("number");
        if (!string.IsNullOrWhiteSpace(number)) room.Number = number;
        doc.Regenerate();
        if (room.Area <= 0) throw new InvalidOperationException("房间未形成有效闭合区域，事务将回滚。");
        var area = room.Area * 0.09290304;
        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit 未提交房间创建事务。");
        return new { committed = true, roomId = room.Id.Value, wallIds = walls, room.Name, room.Number, areaM2 = area };
    }

    private static double Dimension(JsonElement p, string key, double min, double max)
    {
        var v = p.GetRequiredDouble(key);
        if (!double.IsFinite(v) || v < min || v > max) throw new InvalidOperationException($"{key} 必须为 {min}–{max} mm。");
        return v;
    }
}
