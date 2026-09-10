using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitCodexBridge.Addin;

internal static class SteelPlatformTools
{
    private sealed record GridSource(Document Document, Transform Transform, long? LinkInstanceId);
    private sealed record Region(GridFrame Frame, long? LinkInstanceId, long[] GridIds, string[] Names);
    private sealed record Preview(string DocumentToken, JsonElement Parameters, PlatformScheme Scheme, string Signature, DateTime Created);
    private static readonly Dictionary<string, Preview> Previews = new(); // Accessed only in Revit ExternalEvent.

    public static object ListGrids(Document doc)
    {
        object Rows(GridSource source) => new
        {
            source.LinkInstanceId, title = source.Document.Title,
            grids = new FilteredElementCollector(source.Document).OfClass(typeof(Grid)).Cast<Grid>().OrderBy(g => g.Name)
                .Select(g => new { id = g.Id.Value, name = g.Name, straight = g.Curve is Line }).ToArray()
        };
        return new { sources = Sources(doc).Select(Rows).ToArray(), note = "轴线名称按各模型分别列出；链接实例坐标在解析时转换到当前模型。" };
    }

    public static object ResolveRegion(Document doc, JsonElement p)
    {
        var r = Resolve(doc, p);
        return new { r.LinkInstanceId, gridIds = r.GridIds, names = r.Names,
            widthBetweenABmm = r.Frame.WidthMm, lengthBetween12mm = r.Frame.LengthMm,
            intersectionA1mm = r.Frame.Origin, uDirection = r.Frame.U, vDirection = r.Frame.V,
            cornersMm = new[] { r.Frame.Origin, r.Frame.At(r.Frame.WidthMm, 0), r.Frame.At(r.Frame.WidthMm, r.Frame.LengthMm), r.Frame.At(0, r.Frame.LengthMm) },
            note = "世界坐标使用当前模型内部坐标(mm)。U跨A/B轴，V跨1/2轴。高度参考标高另行确认。" };
    }

    public static object Types(Document doc, JsonElement p)
    {
        var offset = (int)Math.Clamp(p.GetOptionalDouble("offset", 0), 0, int.MaxValue);
        var limit = (int)Math.Clamp(p.GetOptionalDouble("limit", 30), 1, 60);
        object Symbols(BuiltInCategory category)
        {
            var items = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(category).Cast<FamilySymbol>().OrderBy(x => x.Id.Value).ToList();
            return new { total = items.Count, hasMore = offset + limit < items.Count, types = items.Skip(offset).Take(limit).Select(s => new
            {
                id = s.Id.Value, family = s.FamilyName, name = s.Name, placement = s.Family.FamilyPlacementType.ToString(),
                depthMm = Depth(s), material = MaterialName(doc, s), structuralMaterial = s.Family.StructuralMaterialType.ToString(),
                note = "截面高度来自族参数，缺失为null；不代表此截面已通过承载力验算。"
            }).ToArray() };
        }
        var floors = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().Where(f => !f.IsFoundationSlab).OrderBy(f => f.Id.Value).ToList();
        return new { offset, limit, columns = Symbols(BuiltInCategory.OST_StructuralColumns), beams = Symbols(BuiltInCategory.OST_StructuralFraming),
            floors = new { total = floors.Count, hasMore = offset + limit < floors.Count,
                types = floors.Skip(offset).Take(limit).Select(f => new { id = f.Id.Value, name = f.Name, thicknessMm = f.GetCompoundStructure()?.GetWidth() * 304.8 }).ToArray() },
            note = "只使用当前模型已加载的类型。按材料/族名称核实钢构件；类型缺失时需加载族，不能编造类型ID。" };
    }

    public static object PreviewPlatform(Document doc, JsonElement p)
    {
        if (doc.IsFamilyDocument || doc.IsReadOnly) throw new InvalidOperationException("需要可写项目文档。");
        if (p.GetRequiredString("supportMode") != "independent") throw new InvalidOperationException("当前自动建模支持独立立柱平台；依附既有结构需要另行设计连接节点。");
        var basis = p.GetRequiredString("designBasis");
        if (basis != "concept") throw new InvalidOperationException("仅支持concept方案模型，未实现承载力优化求解。");
        var loadNotes = p.GetRequiredString("loadNotes");
        if (string.IsNullOrWhiteSpace(loadNotes)) throw new InvalidOperationException("请记录设备/操作荷载，或用户明确同意的‘荷载待定，仅概念布置’。");
        var region = Resolve(doc, p);
        var direction = p.GetRequiredString("sideDirection");
        if (direction is not "parallelA" and not "parallel1") throw new InvalidOperationException("sideDirection须为parallelA（沿A/B轴）或parallel1（沿1/2轴）。");
        var frame = direction == "parallelA" ? region.Frame : region.Frame.Swap();
        var level = doc.GetElement(new ElementId(p.GetRequiredInt64("levelId"))) as Level ?? throw new InvalidOperationException("基准标高不存在。");
        var column = Symbol(doc, p, "columnTypeId", BuiltInCategory.OST_StructuralColumns);
        var beam = Symbol(doc, p, "beamTypeId", BuiltInCategory.OST_StructuralFraming);
        var floor = doc.GetElement(new ElementId(p.GetRequiredInt64("floorTypeId"))) as FloorType ?? throw new InvalidOperationException("平台板类型不存在。");
        if (floor.IsFoundationSlab) throw new InvalidOperationException("请选择普通平台楼板类型，不能使用基础板类型。");
        var thickness = floor.GetCompoundStructure()?.GetWidth() * 304.8 ?? 0;
        var beamDepth = Depth(beam) ?? throw new InvalidOperationException("该梁类型缺少可读取的截面高度，无法核实梁底净高；请选择有截面高度参数的梁族。");
        var heightLimit = p.GetRequiredDouble("maxBeamDepthMm");
        if (!double.IsFinite(heightLimit) || heightLimit <= 0 || beamDepth > heightLimit) throw new InvalidOperationException("所选梁高度超过已确认的梁高上限。");
        var low = p.GetRequiredDouble("sideTopMm"); var high = p.GetRequiredDouble("equipmentTopMm");
        var foundation = p.GetRequiredDouble("foundationOffsetMm");
        if (beamDepth + thickness >= low - foundation) throw new InvalidOperationException("低平台梁底已低于柱底，所选截面/平台高度不适用。");
        var schemes = new List<object>();
        foreach (var expired in Previews.Where(x => DateTime.UtcNow - x.Value.Created > TimeSpan.FromMinutes(30)).Select(x => x.Key).ToArray()) Previews.Remove(expired);
        foreach (var bays in new[] { 1, 2, 3 })
        {
            var scheme = PlatformLayout.Build(frame, p.GetRequiredDouble("equipmentWidthMm"), p.GetRequiredDouble("equipmentLengthMm"),
                p.GetRequiredDouble("sideWidthMm"), low, high, thickness, foundation, bays, p.GetRequiredDouble("secondarySpacingMm"));
            var previewId = Guid.NewGuid().ToString("N");
            Previews[previewId] = new(DesignAgentTools.Token(doc), p.Clone(), scheme, Signature(region, level, column, beam, floor), DateTime.UtcNow);
            schemes.Add(new { previewId, scheme.Bays, scheme.ColumnCount, beamCount = scheme.Members.Count(m => m.Kind == "beam"), deckCount = 3,
                scheme.LongitudinalSpanMm, scheme.MaxBeamSpanMm, beamDepthMm = beamDepth, operatingClearBelowBeamMm = low - thickness - beamDepth,
                sideTopMm = low, equipmentTopMm = high, widthMm = scheme.WidthMm, lengthMm = scheme.LengthMm,
                note = "几何分跨比较，不是承载力或最小梁高结论；增加纵向柱不一定减小中间设备带的横向梁跨度。" });
        }
        while (Previews.Count > 30) Previews.Remove(Previews.OrderBy(x => x.Value.Created).First().Key);
        return new { schemes, level = level.Name, loadNotes, sideDirection = direction, columnType = column.Name, beamType = beam.Name, floorType = floor.Name,
            nearby = Nearby(doc, frame, level.Elevation + foundation / 304.8, level.Elevation + high / 304.8),
            note = "平台居中于所选轴网；两侧板顶和中间板顶按基准标高偏移。独立立柱+梁+三块板；不含基础、节点、支撑、楼梯、栏杆。荷载仅记录，未做强度/挠度/稳定验算；确认几何方案后使用previewId建模。" };
    }

    public static object Create(Document doc, JsonElement p)
    {
        var id = p.GetRequiredString("previewId");
        if (!Previews.TryGetValue(id, out var preview) || preview.DocumentToken != DesignAgentTools.Token(doc)
            || DateTime.UtcNow - preview.Created > TimeSpan.FromMinutes(30)) throw new InvalidOperationException("方案已失效或属于其他文档，请重新preview_steel_platform。");
        var inputs = preview.Parameters;
        var region = Resolve(doc, inputs);
        var level = doc.GetElement(new ElementId(inputs.GetRequiredInt64("levelId"))) as Level ?? throw new InvalidOperationException("标高已失效。");
        var column = Symbol(doc, inputs, "columnTypeId", BuiltInCategory.OST_StructuralColumns);
        var beam = Symbol(doc, inputs, "beamTypeId", BuiltInCategory.OST_StructuralFraming);
        var floorType = doc.GetElement(new ElementId(inputs.GetRequiredInt64("floorTypeId"))) as FloorType ?? throw new InvalidOperationException("楼板类型已失效。");
        if (Signature(region, level, column, beam, floorType) != preview.Signature) throw new InvalidOperationException("轴网、标高或类型已变化，请重新生成方案。");
        var dryRun = p.GetOptionalBoolean("dryRun", true);
        if (!dryRun && p.GetOptionalBoolean("confirmInRevit", true) && TaskDialog.Show("创建设备平台", $"将创建 {preview.Scheme.ColumnCount} 根柱及配套梁、三块板。此为未验算的概念模型。", TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
            throw new OperationCanceledException("用户取消建模。");
        using var tx = new Transaction(doc, "AI 设备钢结构平台（概念方案）");
        tx.Start();
        if (!column.IsActive) column.Activate();
        if (!beam.IsActive) beam.Activate();
        doc.Regenerate();
        var created = new List<long>();
        var tops = new List<(Element Element, double TopFeet)>();
        var direction = preview.Scheme.Decks[0].Outline[1] - preview.Scheme.Decks[0].Outline[0];
        var rotation = Math.Atan2(direction.Y, direction.X);
        foreach (var member in preview.Scheme.Members)
        {
            FamilyInstance instance;
            if (member.Kind == "column")
            {
                instance = doc.Create.NewFamilyInstance(Point(member.Start, level.Elevation + member.BottomMm / 304.8), column, level, StructuralType.Column);
                Set(instance, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, level.Id);
                Set(instance, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, level.Id);
                Set(instance, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM, member.BottomMm / 304.8);
                Set(instance, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM, member.TopMm / 304.8);
                var basePoint = Point(member.Start, level.Elevation + member.BottomMm / 304.8);
                ElementTransformUtils.RotateElement(doc, instance.Id, Line.CreateBound(basePoint, basePoint + XYZ.BasisZ), rotation);
            }
            else
            {
                var line = Line.CreateBound(Point(member.Start, level.Elevation + member.TopMm / 304.8), Point(member.End, level.Elevation + member.TopMm / 304.8));
                instance = doc.Create.NewFamilyInstance(line, beam, level, StructuralType.Beam);
                Set(instance, BuiltInParameter.YZ_JUSTIFICATION, (int)YZJustificationOption.Uniform);
                Set(instance, BuiltInParameter.Z_JUSTIFICATION, (int)ZJustification.Top);
                Set(instance, BuiltInParameter.Z_OFFSET_VALUE, 0.0);
                // Disable end joining to keep the supplied reference line stable for verification.
                StructuralFramingUtils.DisallowJoinAtEnd(instance, 0);
                StructuralFramingUtils.DisallowJoinAtEnd(instance, 1);
            }
            instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("AI概念设备平台；未结构验算；" + id);
            created.Add(instance.Id.Value);
            tops.Add((instance, level.Elevation + member.TopMm / 304.8));
        }
        foreach (var deck in preview.Scheme.Decks)
        {
            var loop = new CurveLoop();
            for (var i = 0; i < 4; i++) loop.Append(Line.CreateBound(Point(deck.Outline[i], level.Elevation), Point(deck.Outline[(i + 1) % 4], level.Elevation)));
            var floor = Floor.Create(doc, new List<CurveLoop> { loop }, floorType.Id, level.Id, true, null!, 0);
            Set(floor, BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM, deck.TopMm / 304.8);
            floor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("AI概念平台板；未结构验算；" + id);
            created.Add(floor.Id.Value);
            tops.Add((floor, level.Elevation + deck.TopMm / 304.8));
        }
        doc.Regenerate();
        foreach (var top in tops)
        {
            var box = top.Element.get_BoundingBox(null);
            if (box is null || Math.Abs(box.Max.Z - top.TopFeet) * 304.8 > 5)
                throw new InvalidOperationException($"构件 {top.Element.Id.Value} 的实际顶面与目标高度不一致（允许5mm），该族需调整定位参数；本次事务将回滚。");
        }
        if (dryRun)
        {
            if (tx.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("试建事务未正确回滚。");
            return new { dryRun = true, geometryValidated = true, previewId = id, preview.Scheme.ColumnCount, members = preview.Scheme.Members.Count, floorCount = 3, note = "真实族试建与顶高检查通过，已回滚试建构件。" };
        }
        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("平台事务未提交。");
        Previews.Remove(id);
        return new { committed = true, previewId = id, preview.Scheme.ColumnCount, createdElementIds = created,
            note = "已创建概念结构构件；未进行结构验算，未建基础/支撑/连接节点/楼梯栏杆。" };
    }

    private static IEnumerable<GridSource> Sources(Document host)
    {
        yield return new(host, Transform.Identity, null);
        foreach (var link in new FilteredElementCollector(host).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            if (link.GetLinkDocument() is Document linked) yield return new(linked, link.GetTotalTransform(), link.Id.Value);
    }
    private static Region Resolve(Document doc, JsonElement p)
    {
        var names = new[] { p.GetRequiredString("gridA"), p.GetRequiredString("gridB"), p.GetRequiredString("grid1"), p.GetRequiredString("grid2") }.Select(x => x.Trim()).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4) throw new InvalidOperationException("必须指定四根不同轴线。");
        var candidates = new List<Region>();
        foreach (var source in Sources(doc))
        {
            if (p.TryGetProperty("linkInstanceId", out var linkId) && linkId.ValueKind == JsonValueKind.Number && source.LinkInstanceId != linkId.GetInt64()) continue;
            var all = new FilteredElementCollector(source.Document).OfClass(typeof(Grid)).Cast<Grid>().ToList();
            var selected = names.Select(n => all.Where(g => string.Equals(g.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)).ToArray()).ToArray();
            if (selected.Any(x => x.Length != 1)) continue;
            var grids = selected.Select(x => x[0]).ToArray();
            var axes = grids.Select(g =>
            {
                if (g.Curve is not Line line) throw new InvalidOperationException($"轴 {g.Name} 是弧形或多段轴，本版不能按直轴定位。");
                var a = source.Transform.OfPoint(line.GetEndPoint(0)); var b = source.Transform.OfPoint(line.GetEndPoint(1));
                if (Math.Abs(a.Z - b.Z) > 1 / 304.8) throw new InvalidOperationException("轴线变换后不在水平面。");
                return new AxisLine(g.Name, new(a.X * 304.8, a.Y * 304.8), new(b.X * 304.8, b.Y * 304.8));
            }).ToArray();
            candidates.Add(new(PlatformLayout.Resolve(axes[0], axes[1], axes[2], axes[3]), source.LinkInstanceId, grids.Select(g => g.Id.Value).ToArray(), names));
            if (source.LinkInstanceId is null) return candidates[0];
        }
        if (candidates.Count != 1) throw new InvalidOperationException(candidates.Count == 0
            ? "未在同一已加载模型找到这四根轴线。请先list_grids核实名称和链接加载状态。"
            : "多个链接模型含同名轴网，请指定linkInstanceId：" + string.Join(",", candidates.Select(x => x.LinkInstanceId)));
        return candidates[0];
    }
    private static FamilySymbol Symbol(Document doc, JsonElement p, string key, BuiltInCategory category)
    {
        var s = doc.GetElement(new ElementId(p.GetRequiredInt64(key))) as FamilySymbol;
        if (s?.Category?.Id.Value != (long)category) throw new InvalidOperationException(key + "不是有效的目标类别族类型。");
        if (s.Family.StructuralMaterialType != StructuralMaterialType.Steel)
            throw new InvalidOperationException(key + "必须使用结构材料分类为Steel的族；不能将混凝土/木构件作为钢平台。");
        return s;
    }
    private static double? Depth(FamilySymbol symbol)
    {
        var p = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
        return p?.StorageType == StorageType.Double && p.AsDouble() > 0 ? p.AsDouble() * 304.8 : null;
    }
    private static string? MaterialName(Document doc, FamilySymbol symbol)
    {
        var p = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
        return p?.StorageType == StorageType.ElementId ? doc.GetElement(p.AsElementId())?.Name : null;
    }
    private static string Signature(Region region, Level level, FamilySymbol column, FamilySymbol beam, FloorType floor) =>
        JsonSerializer.Serialize(new { region, level = level.Elevation, column = column.UniqueId, columnVersion = column.VersionGuid,
            beam = beam.UniqueId, beamVersion = beam.VersionGuid, depth = Depth(beam), floor = floor.UniqueId, floorVersion = floor.VersionGuid,
            thickness = floor.GetCompoundStructure()?.GetWidth() });
    private static XYZ Point(PlanPoint p, double zFeet) => new(p.X / 304.8, p.Y / 304.8, zFeet);
    private static void Set(Element e, BuiltInParameter key, object value)
    {
        var p = e.get_Parameter(key);
        if (p is null || p.IsReadOnly) throw new InvalidOperationException($"{e.Id.Value} 的 {key} 不可设置，此族不适用于自动平台建模。");
        var success = value switch { ElementId id => p.Set(id), double d => p.Set(d), int n => p.Set(n), _ => false };
        if (!success) throw new InvalidOperationException("参数写入失败：" + key);
    }
    private static object Nearby(Document doc, GridFrame f, double z0, double z1)
    {
        var pts = new[] { f.Origin, f.At(f.WidthMm, 0), f.At(0, f.LengthMm), f.At(f.WidthMm, f.LengthMm) };
        using var outline = new Outline(new XYZ(pts.Min(p => p.X) / 304.8, pts.Min(p => p.Y) / 304.8, z0), new XYZ(pts.Max(p => p.X) / 304.8, pts.Max(p => p.Y) / 304.8, z1));
        var found = new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(new BoundingBoxIntersectsFilter(outline)).Take(51).ToList();
        return new { elements = found.Take(50).Select(e => new { id = e.Id.Value, name = e.Name, category = e.Category?.Name }).ToArray(),
            truncated = found.Count > 50, note = "仅当前模型区域包围盒粗筛，不是精确碰撞检查；未检查链接内构件、基础承载力和节点。" };
    }
}
