using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;

namespace RevitCodexBridge.Addin;

internal static class EnergyCubeProjectBuilder
{
	private sealed record LevelSpec(string Name, double ElevationMm);

	private sealed record PointMm(double Xmm, double Ymm);

	private sealed record RoomSpec(string Number, string Name, string LevelName, double Xmm, double Ymm);

	private sealed record EnergyCubeTypes(WallType ExteriorWallType, WallType InteriorWallType, WallType GlassWallType, FloorType FloorType, FamilySymbol DoorSymbol, FamilySymbol WindowSymbol);

	private sealed class EnergyCubeReport
	{
		public List<object> CreatedLevels { get; } = new List<object>();

		public List<object> ReusedLevels { get; } = new List<object>();

		public List<object> CreatedFloors { get; } = new List<object>();

		public List<object> CreatedWalls { get; } = new List<object>();

		public List<object> CreatedDoors { get; } = new List<object>();

		public List<object> CreatedWindows { get; } = new List<object>();

		public List<object> CreatedRooms { get; } = new List<object>();

		public List<object> CreatedGenericModels { get; } = new List<object>();

		public List<string> Warnings { get; } = new List<string>();

		public object ToResult()
		{
			return new
			{
				dryRun = false,
				counts = new
				{
					createdLevels = CreatedLevels.Count,
					reusedLevels = ReusedLevels.Count,
					createdFloors = CreatedFloors.Count,
					createdWalls = CreatedWalls.Count,
					createdDoors = CreatedDoors.Count,
					createdWindows = CreatedWindows.Count,
					createdRooms = CreatedRooms.Count,
					createdGenericModels = CreatedGenericModels.Count,
					warnings = Warnings.Count
				},
				createdLevels = CreatedLevels,
				reusedLevels = ReusedLevels,
				createdFloors = CreatedFloors,
				createdWalls = CreatedWalls,
				createdDoors = CreatedDoors,
				createdWindows = CreatedWindows,
				createdRooms = CreatedRooms,
				createdGenericModels = CreatedGenericModels,
				warnings = Warnings
			};
		}
	}

	private const double SiteMinXmm = -20000.0;

	private const double SiteMaxXmm = 20000.0;

	private const double SiteMinYmm = -30000.0;

	private const double SiteMaxYmm = 30000.0;

	public static object Create(Document document, JsonElement payload)
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Expected O, but got Unknown
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		if (payload.GetOptionalBoolean("dryRun", defaultValue: true))
		{
			return new
			{
				dryRun = true,
				concept = "Energy Cube energy exhibition and smart operation center",
				proposed = new
				{
					site = "40m x 60m",
					levels = 5,
					floors = 4,
					wallSegments = 63,
					rooms = 26,
					doors = 14,
					windows = 56,
					roofEquipmentUnits = 10,
					storageTanks = 2,
					solarArrays = 2,
					exteriorStairSteps = 60,
					landscapeAndSiteObjects = 17
				}
			};
		}
		EnergyCubeReport energyCubeReport = new EnergyCubeReport();
		TransactionGroup val = new TransactionGroup(document, "Codex create Energy Cube model");
		try
		{
			val.Start();
			Transaction val2 = new Transaction(document, "Codex create Energy Cube model");
			try
			{
				val2.Start();
				Dictionary<string, Level> levels = CreateLevels(document, energyCubeReport);
				EnergyCubeTypes types = ResolveTypes(document);
				CreateSiteAndLandscape(document, energyCubeReport);
				CreateFloorSlabs(document, levels, types, energyCubeReport);
				Dictionary<string, Wall> walls = CreateArchitecturalWalls(document, levels, types, energyCubeReport);
				document.Regenerate();
				CreateDoorsAndWindows(document, levels, types, walls, energyCubeReport);
				CreateRooms(document, levels, energyCubeReport);
				CreateRoofObjects(document, energyCubeReport);
				val2.Commit();
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
			val.Assimilate();
			return energyCubeReport.ToResult();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static Dictionary<string, Level> CreateLevels(Document document, EnergyCubeReport report)
	{
		LevelSpec[] array = new LevelSpec[5]
		{
			new LevelSpec("标高 1", 0.0),
			new LevelSpec("标高 2", 4000.0),
			new LevelSpec("标高 3", 8000.0),
			new LevelSpec("屋顶", 12000.0),
			new LevelSpec("设备顶", 24000.0)
		};
		Dictionary<string, Level> dictionary = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
		LevelSpec[] array2 = array;
		foreach (LevelSpec levelSpec in array2)
		{
			Level val = FindLevel(document, levelSpec.Name);
			if (val == null)
			{
				val = Level.Create(document, MmToFeet(levelSpec.ElevationMm));
				((Element)val).Name = levelSpec.Name;
				report.CreatedLevels.Add(new
				{
					id = ((Element)val).Id.Value,
					Name = ((Element)val).Name,
					elevationMm = levelSpec.ElevationMm
				});
			}
			else
			{
				report.ReusedLevels.Add(new
				{
					id = ((Element)val).Id.Value,
					Name = ((Element)val).Name,
					elevationMm = FeetToMm(val.Elevation)
				});
			}
			dictionary[levelSpec.Name] = val;
		}
		return dictionary;
	}

	private static EnergyCubeTypes ResolveTypes(Document document)
	{
		return new EnergyCubeTypes(ResolveWallType(document, new string[3] { "外部 - 带砖与金属立筋龙骨复合墙", "常规 - 200mm", "常规 - 200mm - 实心" }, (WallType type) => (int)type.Kind == 0), ResolveWallType(document, new string[3] { "内部 - 砌块墙 100", "内部 - 135mm 隔断(2 小时)", "常规 - 140mm 砌体" }, (WallType type) => (int)type.Kind == 0), ResolveWallType(document, new string[3] { "外部玻璃", "店面", "幕墙" }, (WallType type) => (int)type.Kind == 1), ResolveFloorType(document), ResolveFamilySymbol(document, (BuiltInCategory)(-2000023), "单扇 - 与墙齐", "750 x 2000mm"), ResolveFamilySymbol(document, (BuiltInCategory)(-2000014), "固定", "1500 x 1500mm"));
	}

	private static void CreateSiteAndLandscape(Document document, EnergyCubeReport report)
	{
		CreateBox(document, "40m x 60m 场地硬化", -20000.0, -30000.0, -150.0, 20000.0, 30000.0, 0.0, report);
		(double, double, double, double)[] array = new(double, double, double, double)[10]
		{
			(-16500.0, -26000.0, -14500.0, -23000.0),
			(-9500.0, -26000.0, -7500.0, -23000.0),
			(7500.0, -26000.0, 9500.0, -23000.0),
			(14500.0, -26000.0, 16500.0, -23000.0),
			(-18500.0, -12000.0, -16500.0, -9000.0),
			(-18500.0, 2000.0, -16500.0, 5000.0),
			(-18500.0, 16000.0, -16500.0, 19000.0),
			(16500.0, 22000.0, 18500.0, 25000.0),
			(8000.0, 25000.0, 11000.0, 27000.0),
			(-11000.0, 25000.0, -8000.0, 27000.0)
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (minXmm, minYmm, maxXmm, maxYmm) = array[i];
			CreateBox(document, $"室外树池 {i + 1}", minXmm, minYmm, 0.0, maxXmm, maxYmm, 450.0, report);
		}
		CreateBox(document, "南侧主入口广场", -8500.0, -30000.0, 8500.0, -21000.0, 80.0, report);
		CreateBox(document, "东侧次入口平台", 18000.0, -6000.0, 20000.0, 8000.0, 80.0, report);
		CreateBox(document, "北侧设备车道", -16000.0, 21000.0, 16000.0, 30000.0, 60.0, report);
	}

	private static void CreateFloorSlabs(Document document, IReadOnlyDictionary<string, Level> levels, EnergyCubeTypes types, EnergyCubeReport report)
	{
		CreateFloor(document, types.FloorType, levels["标高 1"], RectanglePoints(-18000.0, -20000.0, 18000.0, 20000.0), "一层结构楼板", report);
		CreateFloor(document, types.FloorType, levels["标高 2"], RectanglePoints(-18000.0, -20000.0, 18000.0, 20000.0), "二层结构楼板", report);
		CreateFloor(document, types.FloorType, levels["标高 3"], LShapePoints(), "三层退台楼板", report);
		CreateFloor(document, types.FloorType, levels["屋顶"], LShapePoints(), "屋顶设备平台楼板", report);
	}

	private static Dictionary<string, Wall> CreateArchitecturalWalls(Document document, IReadOnlyDictionary<string, Level> levels, EnergyCubeTypes types, EnergyCubeReport report)
	{
		Dictionary<string, Wall> dictionary = new Dictionary<string, Wall>(StringComparer.OrdinalIgnoreCase);
		CreateRectLevel(document, levels["标高 1"], 4000.0, -18000.0, -20000.0, 18000.0, 20000.0, "L1", types, dictionary, report);
		CreateRectLevel(document, levels["标高 2"], 4000.0, -18000.0, -20000.0, 18000.0, 20000.0, "L2", types, dictionary, report);
		CreateLShapeLevel(document, levels["标高 3"], 4000.0, "L3", types, dictionary, report);
		CreateParapets(document, levels["屋顶"], types, dictionary, report);
		return dictionary;
	}

	private static void CreateRectLevel(Document document, Level level, double heightMm, double minX, double minY, double maxX, double maxY, string prefix, EnergyCubeTypes types, IDictionary<string, Wall> walls, EnergyCubeReport report)
	{
		walls[prefix + "-south-west"] = CreateWall(document, level, types.ExteriorWallType, minX, minY, -7000.0, minY, heightMm, prefix + " 南立面实体墙西段", report);
		walls[prefix + "-south-glass"] = CreateWall(document, level, types.ExteriorWallType, -7000.0, minY, 7000.0, minY, heightMm, prefix + " 南立面主入口玻璃墙段", report);
		walls[prefix + "-south-east"] = CreateWall(document, level, types.ExteriorWallType, 7000.0, minY, maxX, minY, heightMm, prefix + " 南立面实体墙东段", report);
		walls[prefix + "-east"] = CreateWall(document, level, types.ExteriorWallType, maxX, minY, maxX, maxY, heightMm, prefix + " 东立面外墙", report);
		walls[prefix + "-north"] = CreateWall(document, level, types.ExteriorWallType, maxX, maxY, minX, maxY, heightMm, prefix + " 北立面外墙", report);
		walls[prefix + "-west"] = CreateWall(document, level, types.ExteriorWallType, minX, maxY, minX, minY, heightMm, prefix + " 西立面外墙", report);
		CreateWall(document, level, types.InteriorWallType, -7000.0, minY, -7000.0, maxY, heightMm, prefix + " 西侧功能分隔墙", report);
		CreateWall(document, level, types.InteriorWallType, 7000.0, minY, 7000.0, maxY, heightMm, prefix + " 东侧功能分隔墙", report);
		CreateWall(document, level, types.InteriorWallType, minX, -5000.0, maxX, -5000.0, heightMm, prefix + " 南北功能分隔墙-1", report);
		CreateWall(document, level, types.InteriorWallType, minX, 9000.0, maxX, 9000.0, heightMm, prefix + " 南北功能分隔墙-2", report);
	}

	private static void CreateLShapeLevel(Document document, Level level, double heightMm, string prefix, EnergyCubeTypes types, IDictionary<string, Wall> walls, EnergyCubeReport report)
	{
		IReadOnlyList<PointMm> readOnlyList = LShapePoints();
		for (int i = 0; i < readOnlyList.Count; i++)
		{
			PointMm pointMm = readOnlyList[i];
			PointMm pointMm2 = readOnlyList[(i + 1) % readOnlyList.Count];
			walls[$"{prefix}-ext-{i + 1}"] = CreateWall(document, level, types.ExteriorWallType, pointMm.Xmm, pointMm.Ymm, pointMm2.Xmm, pointMm2.Ymm, heightMm, $"{prefix} 退台外墙 {i + 1}", report);
		}
		CreateWall(document, level, types.InteriorWallType, -5000.0, -14000.0, -5000.0, 14000.0, heightMm, prefix + " 主体分隔墙 X1", report);
		CreateWall(document, level, types.InteriorWallType, 7000.0, -6000.0, 7000.0, 14000.0, heightMm, prefix + " 东翼分隔墙", report);
		CreateWall(document, level, types.InteriorWallType, 12000.0, -6000.0, 12000.0, 14000.0, heightMm, prefix + " 东翼核心分隔墙", report);
		CreateWall(document, level, types.InteriorWallType, -16000.0, 0.0, 17000.0, 0.0, heightMm, prefix + " 主体分隔墙 Y1", report);
		CreateWall(document, level, types.InteriorWallType, -16000.0, 8000.0, 17000.0, 8000.0, heightMm, prefix + " 主体分隔墙 Y2", report);
	}

	private static void CreateParapets(Document document, Level roofLevel, EnergyCubeTypes types, IDictionary<string, Wall> walls, EnergyCubeReport report)
	{
		IReadOnlyList<PointMm> readOnlyList = LShapePoints();
		for (int i = 0; i < readOnlyList.Count; i++)
		{
			PointMm pointMm = readOnlyList[i];
			PointMm pointMm2 = readOnlyList[(i + 1) % readOnlyList.Count];
			walls[$"roof-parapet-{i + 1}"] = CreateWall(document, roofLevel, types.ExteriorWallType, pointMm.Xmm, pointMm.Ymm, pointMm2.Xmm, pointMm2.Ymm, 1200.0, $"屋顶女儿墙 {i + 1}", report);
		}
		CreateWall(document, roofLevel, types.InteriorWallType, -14000.0, -11000.0, 5000.0, -11000.0, 1200.0, "屋顶光伏区矮墙", report);
		CreateWall(document, roofLevel, types.InteriorWallType, 5000.0, 9000.0, 17000.0, 9000.0, 1200.0, "屋顶设备区矮墙", report);
	}

	private static void CreateDoorsAndWindows(Document document, IReadOnlyDictionary<string, Level> levels, EnergyCubeTypes types, IReadOnlyDictionary<string, Wall> walls, EnergyCubeReport report)
	{
		ActivateSymbol(document, types.DoorSymbol);
		ActivateSymbol(document, types.WindowSymbol);
		PlaceDoor(document, types.DoorSymbol, walls["L1-south-glass"], levels["标高 1"], -2400.0, -20000.0, 0.0, "南侧主入口门-西", report);
		PlaceDoor(document, types.DoorSymbol, walls["L1-south-glass"], levels["标高 1"], 0.0, -20000.0, 0.0, "南侧主入口门-中", report);
		PlaceDoor(document, types.DoorSymbol, walls["L1-south-glass"], levels["标高 1"], 2400.0, -20000.0, 0.0, "南侧主入口门-东", report);
		PlaceDoor(document, types.DoorSymbol, walls["L1-east"], levels["标高 1"], 18000.0, -3000.0, 0.0, "东侧次入口门", report);
		string[] array = new string[2] { "L1", "L2" };
		foreach (string text in array)
		{
			Level val = ((text == "L1") ? levels["标高 1"] : levels["标高 2"]);
			double num = FeetToMm(val.Elevation);
			AddWindowRow(document, types.WindowSymbol, walls[text + "-south-glass"], val, -5400.0, 5400.0, 1800.0, -20000.0, num + 1200.0, horizontal: true, text + " 南立面展示窗", report);
			AddWindowRow(document, types.WindowSymbol, walls[text + "-east"], val, -15000.0, 15000.0, 5000.0, 18000.0, num + 1200.0, horizontal: false, text + " 东立面采光窗", report);
			AddWindowRow(document, types.WindowSymbol, walls[text + "-north"], val, -13000.0, 13000.0, 5200.0, 20000.0, num + 1200.0, horizontal: true, text + " 北立面采光窗", report);
			AddWindowRow(document, types.WindowSymbol, walls[text + "-west"], val, -15000.0, 15000.0, 5000.0, -18000.0, num + 1200.0, horizontal: false, text + " 西立面采光窗", report);
		}
		AddWindowRow(document, types.WindowSymbol, walls["L3-ext-1"], levels["标高 3"], -12000.0, 4000.0, 4200.0, -14000.0, 9200.0, horizontal: true, "L3 南立面窗", report);
		AddWindowRow(document, types.WindowSymbol, walls["L3-ext-5"], levels["标高 3"], -12000.0, 12000.0, 5200.0, 14000.0, 9200.0, horizontal: true, "L3 北立面窗", report);
		AddWindowRow(document, types.WindowSymbol, walls["L3-ext-4"], levels["标高 3"], -2000.0, 10000.0, 4200.0, 17000.0, 9200.0, horizontal: false, "L3 东立面窗", report);
		AddWindowRow(document, types.WindowSymbol, walls["L3-ext-6"], levels["标高 3"], -9000.0, 9000.0, 4500.0, -16000.0, 9200.0, horizontal: false, "L3 西立面窗", report);
	}

	private static void CreateRooms(Document document, IReadOnlyDictionary<string, Level> levels, EnergyCubeReport report)
	{
		//IL_0480: Unknown result type (might be due to invalid IL or missing references)
		//IL_048a: Expected O, but got Unknown
		RoomSpec[] array = new RoomSpec[26]
		{
			new RoomSpec("101", "前厅接待", "标高 1", -12500.0, -12500.0),
			new RoomSpec("102", "综合展厅", "标高 1", 0.0, -12500.0),
			new RoomSpec("103", "咖啡交流", "标高 1", 12500.0, -12500.0),
			new RoomSpec("104", "能源展示厅", "标高 1", -12500.0, 2000.0),
			new RoomSpec("105", "智慧运营大厅", "标高 1", 0.0, 2000.0),
			new RoomSpec("106", "检修工坊", "标高 1", 12500.0, 2000.0),
			new RoomSpec("107", "变配电间", "标高 1", -12500.0, 14500.0),
			new RoomSpec("108", "储能管理间", "标高 1", 0.0, 14500.0),
			new RoomSpec("109", "楼梯电梯核心", "标高 1", 12500.0, 14500.0),
			new RoomSpec("201", "开放办公区", "标高 2", -12500.0, -12500.0),
			new RoomSpec("202", "培训教室", "标高 2", 0.0, -12500.0),
			new RoomSpec("203", "展示洽谈", "标高 2", 12500.0, -12500.0),
			new RoomSpec("204", "研发实验室", "标高 2", -12500.0, 2000.0),
			new RoomSpec("205", "调度会议室", "标高 2", 0.0, 2000.0),
			new RoomSpec("206", "资料档案", "标高 2", 12500.0, 2000.0),
			new RoomSpec("207", "设备监控室", "标高 2", -12500.0, 14500.0),
			new RoomSpec("208", "运维办公室", "标高 2", 0.0, 14500.0),
			new RoomSpec("209", "二层核心筒", "标高 2", 12500.0, 14500.0),
			new RoomSpec("301", "屋顶展厅", "标高 3", -10000.0, -8000.0),
			new RoomSpec("302", "研发办公室", "标高 3", 1000.0, -8000.0),
			new RoomSpec("303", "多功能厅", "标高 3", -10000.0, 4000.0),
			new RoomSpec("304", "会议中心", "标高 3", 1000.0, 4000.0),
			new RoomSpec("305", "能源数据中心", "标高 3", -10000.0, 11000.0),
			new RoomSpec("306", "运维值班", "标高 3", 1000.0, 11000.0),
			new RoomSpec("307", "东侧连廊", "标高 3", 9500.0, 2000.0),
			new RoomSpec("308", "设备控制室", "标高 3", 14500.0, 10000.0)
		};
		RoomSpec[] array2 = array;
		foreach (RoomSpec roomSpec in array2)
		{
			try
			{
				Level val = levels[roomSpec.LevelName];
				Room val2 = document.Create.NewRoom(val, new UV(MmToFeet(roomSpec.Xmm), MmToFeet(roomSpec.Ymm)));
				SetStringParameter((Element)(object)val2, "名称", roomSpec.Name);
				SetStringParameter((Element)(object)val2, "编号", roomSpec.Number);
				report.CreatedRooms.Add(new
				{
					id = ((Element)val2).Id.Value,
					Number = roomSpec.Number,
					Name = roomSpec.Name,
					level = ((Element)val).Name
				});
			}
			catch (Exception ex)
			{
				report.Warnings.Add($"Could not create room {roomSpec.Number} {roomSpec.Name}: {ex.Message}");
			}
		}
	}

	private static void CreateRoofObjects(Document document, EnergyCubeReport report)
	{
		CreateBox(document, "屋顶光伏阵列 A", -14000.0, -12000.0, 12250.0, 4000.0, -8500.0, 12420.0, report);
		CreateBox(document, "屋顶光伏阵列 B", 9000.0, -4500.0, 12250.0, 16000.0, 7500.0, 12420.0, report);
		for (int i = 0; i < 2; i++)
		{
			for (int j = 0; j < 5; j++)
			{
				int num = -2500 + j * 2300;
				int num2 = 9200 + i * 2100;
				CreateBox(document, $"屋顶能源设备机组 {i * 5 + j + 1}", num, num2, 12300.0, num + 1900, num2 + 1400, 13500.0, report);
			}
		}
		CreateCylinder(document, "圆柱储能罐 1", 6500.0, 13500.0, 12300.0, 2500.0, 8200.0, report);
		CreateCylinder(document, "圆柱储能罐 2", 12500.0, 13500.0, 12300.0, 2500.0, 8200.0, report);
		CreateBox(document, "屋顶花园种植池 A", -15000.0, 2000.0, 12150.0, -12500.0, 9000.0, 12650.0, report);
		CreateBox(document, "屋顶花园种植池 B", -10000.0, 9000.0, 12150.0, -2500.0, 11500.0, 12650.0, report);
		CreateBox(document, "屋顶露台铺装", -15000.0, -4500.0, 12080.0, -2500.0, 1500.0, 12120.0, report);
		CreateExteriorStair(document, report);
	}

	private static void CreateExteriorStair(Document document, EnergyCubeReport report)
	{
		CreateStairFlight(document, 18500.0, -18500.0, -11500.0, 0.0, 4000.0, "东南外部楼梯一跑", report);
		CreateBox(document, "东南外部楼梯一层半平台", 18000.0, -11500.0, 4000.0, 23000.0, -9500.0, 4200.0, report);
		CreateStairFlight(document, 18500.0, -9500.0, -2500.0, 4000.0, 8000.0, "东南外部楼梯二跑", report);
		CreateBox(document, "东南外部楼梯二层平台", 18000.0, -2500.0, 8000.0, 23000.0, -500.0, 8200.0, report);
		CreateStairFlight(document, 18500.0, -500.0, 6500.0, 8000.0, 12000.0, "东南外部楼梯三跑", report);
		CreateBox(document, "东南外部楼梯屋顶平台", 17500.0, 6500.0, 12000.0, 23000.0, 9000.0, 12200.0, report);
	}

	private static void CreateStairFlight(Document document, double minX, double startY, double endY, double startZ, double endZ, string name, EnergyCubeReport report)
	{
		double num = (endY - startY) / 20.0;
		double num2 = (endZ - startZ) / 20.0;
		for (int i = 0; i < 20; i++)
		{
			double val = startY + num * (double)i;
			double val2 = startY + num * (double)(i + 1);
			double num3 = startZ + num2 * (double)i;
			CreateBox(document, $"{name} 踏步 {i + 1}", minX, Math.Min(val, val2), num3, minX + 4200.0, Math.Max(val, val2), num3 + 220.0, report);
		}
	}

	private static Floor? CreateFloor(Document document, FloorType floorType, Level level, IReadOnlyList<PointMm> points, string name, EnergyCubeReport report)
	{
		try
		{
			CurveLoop item = CreateCurveLoop(points, 0.0);
			Floor val = Floor.Create(document, (IList<CurveLoop>)new List<CurveLoop> { item }, ((Element)floorType).Id, ((Element)level).Id);
			TrySetName((Element)(object)val, name);
			report.CreatedFloors.Add(new
			{
				id = ((Element)val).Id.Value,
				name = name,
				level = ((Element)level).Name
			});
			return val;
		}
		catch (Exception ex)
		{
			report.Warnings.Add("Could not create floor " + name + ": " + ex.Message);
			return null;
		}
	}

	private static Wall CreateWall(Document document, Level level, WallType wallType, double startXmm, double startYmm, double endXmm, double endYmm, double heightMm, string name, EnergyCubeReport report)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		//IL_003d: Expected O, but got Unknown
		Line val = Line.CreateBound(new XYZ(MmToFeet(startXmm), MmToFeet(startYmm), 0.0), new XYZ(MmToFeet(endXmm), MmToFeet(endYmm), 0.0));
		Wall val2 = Wall.Create(document, (Curve)(object)val, ((Element)wallType).Id, ((Element)level).Id, MmToFeet(heightMm), 0.0, false, false);
		TrySetName((Element)(object)val2, name);
		report.CreatedWalls.Add(new
		{
			id = ((Element)val2).Id.Value,
			name = name,
			level = ((Element)level).Name,
			heightMm = heightMm
		});
		return val2;
	}

	private static void PlaceDoor(Document document, FamilySymbol symbol, Wall host, Level level, double xMm, double yMm, double zMm, string name, EnergyCubeReport report)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		try
		{
			FamilyInstance val = document.Create.NewFamilyInstance(new XYZ(MmToFeet(xMm), MmToFeet(yMm), MmToFeet(zMm)), symbol, (Element)(object)host, level, (StructuralType)0);
			TrySetName((Element)(object)val, name);
			report.CreatedDoors.Add(new
			{
				id = ((Element)val).Id.Value,
				name = name,
				hostId = ((Element)host).Id.Value
			});
		}
		catch (Exception ex)
		{
			report.Warnings.Add("Could not place door " + name + ": " + ex.Message);
		}
	}

	private static void AddWindowRow(Document document, FamilySymbol symbol, Wall host, Level level, double startMm, double endMm, double spacingMm, double fixedCoordMm, double zMm, bool horizontal, string name, EnergyCubeReport report)
	{
		int num = 1;
		for (double num2 = startMm; num2 <= endMm + 1.0; num2 += spacingMm)
		{
			double xMm = (horizontal ? num2 : fixedCoordMm);
			double yMm = (horizontal ? fixedCoordMm : num2);
			PlaceWindow(document, symbol, host, level, xMm, yMm, zMm, $"{name} {num}", report);
			num++;
		}
	}

	private static void PlaceWindow(Document document, FamilySymbol symbol, Wall host, Level level, double xMm, double yMm, double zMm, string name, EnergyCubeReport report)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		try
		{
			FamilyInstance val = document.Create.NewFamilyInstance(new XYZ(MmToFeet(xMm), MmToFeet(yMm), MmToFeet(zMm)), symbol, (Element)(object)host, level, (StructuralType)0);
			TrySetName((Element)(object)val, name);
			report.CreatedWindows.Add(new
			{
				id = ((Element)val).Id.Value,
				name = name,
				hostId = ((Element)host).Id.Value
			});
		}
		catch (Exception ex)
		{
			report.Warnings.Add("Could not place window " + name + ": " + ex.Message);
		}
	}

	private static void CreateBox(Document document, string name, double minXmm, double minYmm, double maxXmm, double maxYmm, double heightMm, EnergyCubeReport report)
	{
		CreateBox(document, name, minXmm, minYmm, 0.0, maxXmm, maxYmm, heightMm, report);
	}

	private static void CreateBox(Document document, string name, double minXmm, double minYmm, double minZmm, double maxXmm, double maxYmm, double maxZmm, EnergyCubeReport report)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Expected O, but got Unknown
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Expected O, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Expected O, but got Unknown
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Expected O, but got Unknown
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Expected O, but got Unknown
		try
		{
			CurveLoop val = new CurveLoop();
			XYZ val2 = new XYZ(MmToFeet(minXmm), MmToFeet(minYmm), MmToFeet(minZmm));
			XYZ val3 = new XYZ(MmToFeet(maxXmm), MmToFeet(minYmm), MmToFeet(minZmm));
			XYZ val4 = new XYZ(MmToFeet(maxXmm), MmToFeet(maxYmm), MmToFeet(minZmm));
			XYZ val5 = new XYZ(MmToFeet(minXmm), MmToFeet(maxYmm), MmToFeet(minZmm));
			val.Append((Curve)(object)Line.CreateBound(val2, val3));
			val.Append((Curve)(object)Line.CreateBound(val3, val4));
			val.Append((Curve)(object)Line.CreateBound(val4, val5));
			val.Append((Curve)(object)Line.CreateBound(val5, val2));
			Solid item = GeometryCreationUtilities.CreateExtrusionGeometry((IList<CurveLoop>)new List<CurveLoop> { val }, XYZ.BasisZ, MmToFeet(Math.Max(10.0, maxZmm - minZmm)));
			DirectShape val6 = DirectShape.CreateElement(document, new ElementId((BuiltInCategory)(-2000151)));
			val6.ApplicationId = "RevitCodexBridge";
			val6.ApplicationDataId = name;
			TrySetName((Element)(object)val6, name);
			val6.SetShape((IList<GeometryObject>)new List<GeometryObject> { (GeometryObject)(object)item });
			report.CreatedGenericModels.Add(new
			{
				id = ((Element)val6).Id.Value,
				name = name
			});
		}
		catch (Exception ex)
		{
			report.Warnings.Add("Could not create box " + name + ": " + ex.Message);
		}
	}

	private static void CreateCylinder(Document document, string name, double centerXmm, double centerYmm, double baseZmm, double radiusMm, double heightMm, EnergyCubeReport report)
	{
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected O, but got Unknown
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Expected O, but got Unknown
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Expected O, but got Unknown
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected O, but got Unknown
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Expected O, but got Unknown
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Expected O, but got Unknown
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Expected O, but got Unknown
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Expected O, but got Unknown
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Expected O, but got Unknown
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Expected O, but got Unknown
		try
		{
			double num = MmToFeet(centerXmm);
			double num2 = MmToFeet(centerYmm);
			double num3 = MmToFeet(baseZmm);
			double num4 = MmToFeet(radiusMm);
			double num5 = num4 / Math.Sqrt(2.0);
			XYZ val = new XYZ(num + num4, num2, num3);
			XYZ val2 = new XYZ(num, num2 + num4, num3);
			XYZ val3 = new XYZ(num - num4, num2, num3);
			XYZ val4 = new XYZ(num, num2 - num4, num3);
			CurveLoop val5 = new CurveLoop();
			val5.Append((Curve)(object)Arc.Create(val, val2, new XYZ(num + num5, num2 + num5, num3)));
			val5.Append((Curve)(object)Arc.Create(val2, val3, new XYZ(num - num5, num2 + num5, num3)));
			val5.Append((Curve)(object)Arc.Create(val3, val4, new XYZ(num - num5, num2 - num5, num3)));
			val5.Append((Curve)(object)Arc.Create(val4, val, new XYZ(num + num5, num2 - num5, num3)));
			Solid item = GeometryCreationUtilities.CreateExtrusionGeometry((IList<CurveLoop>)new List<CurveLoop> { val5 }, XYZ.BasisZ, MmToFeet(heightMm));
			DirectShape val6 = DirectShape.CreateElement(document, new ElementId((BuiltInCategory)(-2000151)));
			val6.ApplicationId = "RevitCodexBridge";
			val6.ApplicationDataId = name;
			TrySetName((Element)(object)val6, name);
			val6.SetShape((IList<GeometryObject>)new List<GeometryObject> { (GeometryObject)(object)item });
			report.CreatedGenericModels.Add(new
			{
				id = ((Element)val6).Id.Value,
				name = name
			});
		}
		catch (Exception ex)
		{
			report.Warnings.Add("Could not create cylinder " + name + ": " + ex.Message);
		}
	}

	private static IReadOnlyList<PointMm> RectanglePoints(double minX, double minY, double maxX, double maxY)
	{
		return new PointMm[4]
		{
			new PointMm(minX, minY),
			new PointMm(maxX, minY),
			new PointMm(maxX, maxY),
			new PointMm(minX, maxY)
		};
	}

	private static IReadOnlyList<PointMm> LShapePoints()
	{
		return new PointMm[6]
		{
			new PointMm(-16000.0, -14000.0),
			new PointMm(7000.0, -14000.0),
			new PointMm(7000.0, -6000.0),
			new PointMm(17000.0, -6000.0),
			new PointMm(17000.0, 14000.0),
			new PointMm(-16000.0, 14000.0)
		};
	}

	private static CurveLoop CreateCurveLoop(IReadOnlyList<PointMm> points, double zMm)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Expected O, but got Unknown
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected O, but got Unknown
		//IL_006d: Expected O, but got Unknown
		CurveLoop val = new CurveLoop();
		for (int i = 0; i < points.Count; i++)
		{
			PointMm pointMm = points[i];
			PointMm pointMm2 = points[(i + 1) % points.Count];
			val.Append((Curve)(object)Line.CreateBound(new XYZ(MmToFeet(pointMm.Xmm), MmToFeet(pointMm.Ymm), MmToFeet(zMm)), new XYZ(MmToFeet(pointMm2.Xmm), MmToFeet(pointMm2.Ymm), MmToFeet(zMm))));
		}
		return val;
	}

	private static Level? FindLevel(Document document, string name)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		return ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Level))).Cast<Level>().FirstOrDefault((Level level) => string.Equals(((Element)level).Name, name, StringComparison.OrdinalIgnoreCase));
	}

	private static WallType ResolveWallType(Document document, IReadOnlyList<string> preferredNames, Func<WallType, bool> predicate)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		List<WallType> source = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(WallType))).Cast<WallType>().Where(predicate).ToList();
		foreach (string preferredName in preferredNames)
		{
			WallType val = source.FirstOrDefault((WallType type) => string.Equals(((Element)type).Name, preferredName, StringComparison.OrdinalIgnoreCase));
			if (val != null)
			{
				return val;
			}
		}
		return source.FirstOrDefault() ?? ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(WallType))).Cast<WallType>().First() ?? throw new InvalidOperationException("No wall type found.");
	}

	private static FloorType ResolveFloorType(Document document)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		return ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(FloorType))).Cast<FloorType>().FirstOrDefault() ?? throw new InvalidOperationException("No floor type found.");
	}

	private static FamilySymbol ResolveFamilySymbol(Document document, BuiltInCategory category, string familyName, string typeName)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		List<FamilySymbol> source = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).OfCategory(category)).Cast<FamilySymbol>().ToList();
		return source.FirstOrDefault((FamilySymbol symbol) => string.Equals(((ElementType)symbol).FamilyName, familyName, StringComparison.OrdinalIgnoreCase) && string.Equals(((Element)symbol).Name, typeName, StringComparison.OrdinalIgnoreCase)) ?? source.FirstOrDefault() ?? throw new InvalidOperationException($"No family symbol found for {category}.");
	}

	private static void ActivateSymbol(Document document, FamilySymbol symbol)
	{
		if (!symbol.IsActive)
		{
			symbol.Activate();
			document.Regenerate();
		}
	}

	private static void SetStringParameter(Element element, string parameterName, string value)
	{
		Parameter val = element.LookupParameter(parameterName);
		if (val != null && !((APIObject)val).IsReadOnly)
		{
			val.Set(value);
		}
	}

	private static void TrySetName(Element element, string name)
	{
		try
		{
			element.Name = name;
		}
		catch
		{
		}
	}

	private static double MmToFeet(double value)
	{
		return value / 304.8;
	}

	private static double FeetToMm(double value)
	{
		return value * 304.8;
	}
}
