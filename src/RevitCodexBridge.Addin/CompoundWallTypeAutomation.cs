using System.Text.Json;
using Autodesk.Revit.DB;

namespace RevitCodexBridge.Addin;

/// <summary>Creates a basic-wall compound structure and can replace matching wall instances in one transaction.</summary>
internal static class CompoundWallTypeAutomation
{
    private sealed record LayerSpec(string MaterialName, double ThicknessMm, MaterialFunctionAssignment Function);

    public static object Create(Document document, JsonElement payload)
    {
        var dryRun = payload.GetOptionalBoolean("dryRun", defaultValue: true);
        var source = ResolveWallType(document, payload, "sourceWallType");
        if (source.Kind != WallKind.Basic)
        {
            throw new InvalidOperationException($"Wall type '{source.Name}' is not a basic wall type.");
        }

        var newName = payload.GetRequiredString("newWallTypeName").Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new InvalidOperationException("newWallTypeName cannot be empty.");
        }

        var layers = ReadLayers(payload);
        var replacementSource = ResolveOptionalWallType(document, payload, "replaceSourceWallType") ?? source;
        var replaceExisting = HasReplacementSource(payload);
        var wallsToReplace = replaceExisting
            ? new FilteredElementCollector(document)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(wall => wall.GetTypeId() == replacementSource.Id)
                .ToList()
            : [];
        var existingTarget = FindWallTypeByName(document, newName);

        if (existingTarget is not null && !payload.GetOptionalBoolean("reuseExisting", defaultValue: false))
        {
            throw new InvalidOperationException($"Wall type '{newName}' already exists. Set reuseExisting=true to use it.");
        }

        if (dryRun)
        {
            return new
            {
                dryRun = true,
                sourceWallType = Describe(source),
                newWallTypeName = newName,
                targetAlreadyExists = existingTarget is not null,
                layers = layers.Select(Describe),
                totalThicknessMm = layers.Sum(layer => layer.ThicknessMm),
                replaceSourceWallType = replaceExisting ? Describe(replacementSource) : null,
                wallsToReplace = wallsToReplace.Count
            };
        }

        WallType target;
        using (var transaction = new Transaction(document, "YingTan AI create compound wall type"))
        {
            transaction.Start();
            target = existingTarget ?? (source.Duplicate(newName) as WallType
                ?? throw new InvalidOperationException("Revit did not return a wall type from Duplicate."));

            if (existingTarget is null)
            {
                var compoundLayers = layers.Select(layer => new CompoundStructureLayer(
                    UnitUtils.ConvertToInternalUnits(layer.ThicknessMm, UnitTypeId.Millimeters),
                    layer.Function,
                    ResolveOrCreateMaterial(document, layer.MaterialName))).ToList();
                var structure = CompoundStructure.CreateSimpleCompoundStructure(compoundLayers);
                target.SetCompoundStructure(structure);
            }

            foreach (var wall in wallsToReplace)
            {
                wall.ChangeTypeId(target.Id);
            }

            transaction.Commit();
        }

        return new
        {
            dryRun = false,
            sourceWallType = Describe(source),
            targetWallType = Describe(target),
            createdNewType = existingTarget is null,
            layers = layers.Select(Describe),
            totalThicknessMm = layers.Sum(layer => layer.ThicknessMm),
            replaceSourceWallType = replaceExisting ? Describe(replacementSource) : null,
            replacedWallCount = wallsToReplace.Count,
            replacedWallElementIds = wallsToReplace.Select(wall => wall.Id.Value).ToList()
        };
    }

    private static List<LayerSpec> ReadLayers(JsonElement payload)
    {
        if (!payload.TryGetProperty("layers", out var layersNode) || layersNode.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("create_compound_wall_type requires a layers array.");
        }

        var layers = new List<LayerSpec>();
        foreach (var layer in layersNode.EnumerateArray())
        {
            if (layer.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Each wall layer must be an object.");
            }

            var materialName = layer.GetRequiredString("materialName").Trim();
            var thicknessMm = layer.GetRequiredProperty("thicknessMm").GetDouble();
            var functionName = layer.GetOptionalString("function") ?? "Finish1";
            if (string.IsNullOrWhiteSpace(materialName) || thicknessMm <= 0)
            {
                throw new InvalidOperationException("Each wall layer needs a materialName and a positive thicknessMm.");
            }

            if (!Enum.TryParse<MaterialFunctionAssignment>(functionName, true, out var function))
            {
                throw new InvalidOperationException($"Unknown material function '{functionName}'.");
            }

            layers.Add(new LayerSpec(materialName, thicknessMm, function));
        }

        if (layers.Count == 0)
        {
            throw new InvalidOperationException("At least one wall layer is required.");
        }

        return layers;
    }

    private static WallType ResolveWallType(Document document, JsonElement payload, string prefix)
    {
        return ResolveOptionalWallType(document, payload, prefix)
            ?? throw new InvalidOperationException($"Provide {prefix}Name or {prefix}Id.");
    }

    private static WallType? ResolveOptionalWallType(Document document, JsonElement payload, string prefix)
    {
        var idName = prefix + "Id";
        if (payload.TryGetProperty(idName, out var idNode) && idNode.ValueKind == JsonValueKind.Number)
        {
            var type = document.GetElement(new ElementId(idNode.GetInt64())) as WallType;
            return type ?? throw new InvalidOperationException($"Wall type {idNode.GetInt64()} was not found.");
        }

        var name = payload.GetOptionalString(prefix + "Name");
        return string.IsNullOrWhiteSpace(name) ? null : FindWallTypeByName(document, name);
    }

    private static WallType? FindWallTypeByName(Document document, string name)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .FirstOrDefault(type => type.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static ElementId ResolveOrCreateMaterial(Document document, string materialName)
    {
        var existing = new FilteredElementCollector(document)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .FirstOrDefault(material => material.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
        return existing?.Id ?? Material.Create(document, materialName);
    }

    private static bool HasReplacementSource(JsonElement payload)
    {
        return payload.TryGetProperty("replaceSourceWallTypeId", out _) ||
            !string.IsNullOrWhiteSpace(payload.GetOptionalString("replaceSourceWallTypeName"));
    }

    private static object Describe(WallType type)
    {
        return new { id = type.Id.Value, name = type.Name, kind = type.Kind.ToString() };
    }

    private static object Describe(LayerSpec layer)
    {
        return new { materialName = layer.MaterialName, thicknessMm = layer.ThicknessMm, function = layer.Function.ToString() };
    }
}
