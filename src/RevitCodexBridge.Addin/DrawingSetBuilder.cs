using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using View = Autodesk.Revit.DB.View;

namespace RevitCodexBridge.Addin;

internal static class DrawingSetBuilder
{
	private sealed record AxisSpec(string Name, double ValueMm);

	private sealed record PlanSpec(Level Level, string ViewName);

	private sealed class RoomElevationGroup
	{
		public Room Room { get; }

		public List<ViewSection> Views { get; } = new List<ViewSection>();

		public RoomElevationGroup(Room room)
		{
			Room = room;
		}
	}

	private sealed class DrawingSettings
	{
		public double MinXmm { get; init; }

		public double MaxXmm { get; init; }

		public double MinYmm { get; init; }

		public double MaxYmm { get; init; }

		public double TopMm { get; init; }

		public double GridPaddingMm { get; init; }

		public double DimensionOffsetMm { get; init; }

		public double OverallDimensionOffsetMm { get; init; }

		public double ElevationPaddingMm { get; init; }

		public double ElevationOffsetMm { get; init; }

		public double RoomElevationPaddingMm { get; init; }

		public double SecondFloorHeightMm { get; init; }

		public int PlanScale { get; init; }

		public int ElevationScale { get; init; }

		public int RoomElevationScale { get; init; }

		public IReadOnlyList<string> LevelNames { get; init; } = Array.Empty<string>();

		public IReadOnlyList<AxisSpec> VerticalAxes { get; init; } = Array.Empty<AxisSpec>();

		public IReadOnlyList<AxisSpec> HorizontalAxes { get; init; } = Array.Empty<AxisSpec>();

		public static DrawingSettings Read(JsonElement payload)
		{
			return new DrawingSettings
			{
				MinXmm = payload.GetOptionalDouble("minXmm", -6250.0),
				MaxXmm = payload.GetOptionalDouble("maxXmm", 6250.0),
				MinYmm = payload.GetOptionalDouble("minYmm", -6000.0),
				MaxYmm = payload.GetOptionalDouble("maxYmm", 6000.0),
				TopMm = payload.GetOptionalDouble("topMm", 7200.0),
				GridPaddingMm = payload.GetOptionalDouble("gridPaddingMm", 1200.0),
				DimensionOffsetMm = payload.GetOptionalDouble("dimensionOffsetMm", 850.0),
				OverallDimensionOffsetMm = payload.GetOptionalDouble("overallDimensionOffsetMm", 1450.0),
				ElevationPaddingMm = payload.GetOptionalDouble("elevationPaddingMm", 1000.0),
				ElevationOffsetMm = payload.GetOptionalDouble("elevationOffsetMm", 1500.0),
				RoomElevationPaddingMm = payload.GetOptionalDouble("roomElevationPaddingMm", 600.0),
				SecondFloorHeightMm = payload.GetOptionalDouble("secondFloorHeightMm", 3200.0),
				PlanScale = (int)payload.GetOptionalDouble("planScale", 100.0),
				ElevationScale = (int)payload.GetOptionalDouble("elevationScale", 100.0),
				RoomElevationScale = (int)payload.GetOptionalDouble("roomElevationScale", 50.0),
				LevelNames = ReadStringArray(payload, "levelNames", new string[2] { "标高 1", "标高 2" }),
				VerticalAxes = ReadAxes(payload, "verticalAxes", "xMm", new AxisSpec[4]
				{
					new AxisSpec("1", -6250.0),
					new AxisSpec("2", -1800.0),
					new AxisSpec("3", 2200.0),
					new AxisSpec("4", 6250.0)
				}),
				HorizontalAxes = ReadAxes(payload, "horizontalAxes", "yMm", new AxisSpec[5]
				{
					new AxisSpec("A", -6000.0),
					new AxisSpec("B", -2000.0),
					new AxisSpec("C", 1500.0),
					new AxisSpec("D", 3500.0),
					new AxisSpec("E", 6000.0)
				})
			};
		}

		private static IReadOnlyList<string> ReadStringArray(JsonElement payload, string propertyName, IReadOnlyList<string> defaultValue)
		{
			if (!payload.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
			{
				return defaultValue;
			}
			return (from jsonElement in value.EnumerateArray()
				where jsonElement.ValueKind == JsonValueKind.String
				select jsonElement.GetString() into value2
				where !string.IsNullOrWhiteSpace(value2)
				select value2).Cast<string>().ToList();
		}

		private static IReadOnlyList<AxisSpec> ReadAxes(JsonElement payload, string propertyName, string coordinateName, IReadOnlyList<AxisSpec> defaultValue)
		{
			if (!payload.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
			{
				return defaultValue;
			}
			List<AxisSpec> list = new List<AxisSpec>();
			foreach (JsonElement item in value.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object)
				{
					string optionalString = item.GetOptionalString("name");
					if (!string.IsNullOrWhiteSpace(optionalString) && item.TryGetProperty(coordinateName, out var value2))
					{
						list.Add(new AxisSpec(optionalString, value2.GetDouble()));
					}
				}
			}
			IReadOnlyList<AxisSpec> result;
			if (list.Count != 0)
			{
				IReadOnlyList<AxisSpec> readOnlyList = list;
				result = readOnlyList;
			}
			else
			{
				result = defaultValue;
			}
			return result;
		}
	}

	private sealed class DrawingSetReport
	{
		private bool DryRun { get; }

		public List<object> CreatedGrids { get; } = new List<object>();

		public List<object> ReusedGrids { get; } = new List<object>();

		public List<object> CreatedViews { get; } = new List<object>();

		public List<object> ReusedViews { get; } = new List<object>();

		public List<object> CreatedDimensions { get; } = new List<object>();

		public List<object> CreatedSheets { get; } = new List<object>();

		public List<object> ReusedSheets { get; } = new List<object>();

		public List<object> CreatedViewports { get; } = new List<object>();

		public List<string> Warnings { get; } = new List<string>();

		public DrawingSetReport(bool dryRun)
		{
			DryRun = dryRun;
		}

		public object ToResult()
		{
			return new
			{
				dryRun = DryRun,
				counts = new
				{
					createdGrids = CreatedGrids.Count,
					reusedGrids = ReusedGrids.Count,
					createdViews = CreatedViews.Count,
					reusedViews = ReusedViews.Count,
					createdDimensions = CreatedDimensions.Count,
					createdSheets = CreatedSheets.Count,
					reusedSheets = ReusedSheets.Count,
					createdViewports = CreatedViewports.Count,
					warnings = Warnings.Count
				},
				createdGrids = CreatedGrids,
				reusedGrids = ReusedGrids,
				createdViews = CreatedViews,
				reusedViews = ReusedViews,
				createdDimensions = CreatedDimensions,
				createdSheets = CreatedSheets,
				reusedSheets = ReusedSheets,
				createdViewports = CreatedViewports,
				warnings = Warnings
			};
		}
	}

	private const double DefaultMinXmm = -6250.0;

	private const double DefaultMaxXmm = 6250.0;

	private const double DefaultMinYmm = -6000.0;

	private const double DefaultMaxYmm = 6000.0;

	private const double DefaultTopMm = 7200.0;

	public static object Create(Document document, JsonElement payload)
	{
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Expected O, but got Unknown
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Expected O, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Expected O, but got Unknown
		//IL_0114: Expected O, but got Unknown
		//IL_018f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c7: Expected O, but got Unknown
		//IL_01c7: Expected O, but got Unknown
		//IL_0249: Unknown result type (might be due to invalid IL or missing references)
		//IL_0265: Unknown result type (might be due to invalid IL or missing references)
		//IL_026c: Expected O, but got Unknown
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d9: Expected O, but got Unknown
		//IL_02dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_035f: Unknown result type (might be due to invalid IL or missing references)
		//IL_037b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0382: Expected O, but got Unknown
		//IL_0385: Unknown result type (might be due to invalid IL or missing references)
		//IL_039c: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b4: Unknown result type (might be due to invalid IL or missing references)
		bool optionalBoolean = payload.GetOptionalBoolean("dryRun", defaultValue: true);
		DrawingSettings drawingSettings = DrawingSettings.Read(payload);
		IReadOnlyList<Room> readOnlyList = CollectRooms(document);
		IReadOnlyList<PlanSpec> readOnlyList2 = ResolvePlanSpecs(document, drawingSettings);
		DrawingSetReport drawingSetReport = new DrawingSetReport(optionalBoolean);
		if (optionalBoolean)
		{
			return BuildDryRunReport(document, drawingSettings, readOnlyList, readOnlyList2);
		}
		Dictionary<string, Grid> dictionary = new Dictionary<string, Grid>(StringComparer.OrdinalIgnoreCase);
		List<ViewPlan> list = new List<ViewPlan>();
		List<ViewSection> list2 = new List<ViewSection>();
		List<RoomElevationGroup> list3 = new List<RoomElevationGroup>();
		TransactionGroup val = new TransactionGroup(document, "Codex create drawing set");
		try
		{
			val.Start();
			Transaction val2 = new Transaction(document, "Codex create grids and plan views");
			try
			{
				val2.Start();
				foreach (AxisSpec verticalAxis in drawingSettings.VerticalAxes)
				{
					Grid value = CreateOrReuseGrid(document, verticalAxis.Name, new XYZ(MmToFeet(verticalAxis.ValueMm), MmToFeet(drawingSettings.MinYmm - drawingSettings.GridPaddingMm), 0.0), new XYZ(MmToFeet(verticalAxis.ValueMm), MmToFeet(drawingSettings.MaxYmm + drawingSettings.GridPaddingMm), 0.0), drawingSetReport);
					dictionary[verticalAxis.Name] = value;
				}
				foreach (AxisSpec horizontalAxis in drawingSettings.HorizontalAxes)
				{
					Grid value2 = CreateOrReuseGrid(document, horizontalAxis.Name, new XYZ(MmToFeet(drawingSettings.MinXmm - drawingSettings.GridPaddingMm), MmToFeet(horizontalAxis.ValueMm), 0.0), new XYZ(MmToFeet(drawingSettings.MaxXmm + drawingSettings.GridPaddingMm), MmToFeet(horizontalAxis.ValueMm), 0.0), drawingSetReport);
					dictionary[horizontalAxis.Name] = value2;
				}
				ViewFamilyType floorPlanType = ResolveViewFamilyType(document, (ViewFamily)109);
				foreach (PlanSpec item2 in readOnlyList2)
				{
					ViewPlan item = CreateOrReusePlanView(document, floorPlanType, item2, drawingSettings, drawingSetReport);
					list.Add(item);
				}
				val2.Commit();
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
			Transaction val3 = new Transaction(document, "Codex create dimensions");
			try
			{
				val3.Start();
				foreach (ViewPlan item3 in list)
				{
					CreateGridDimensions(document, item3, drawingSettings, dictionary, drawingSetReport);
				}
				val3.Commit();
			}
			finally
			{
				((IDisposable)val3)?.Dispose();
			}
			Transaction val4 = new Transaction(document, "Codex create elevation views");
			try
			{
				val4.Start();
				ViewFamilyType sectionType = ResolveViewFamilyType(document, (ViewFamily)112);
				list2.AddRange(CreateBuildingElevations(document, sectionType, drawingSettings, drawingSetReport));
				foreach (Room item4 in readOnlyList)
				{
					RoomElevationGroup roomElevationGroup = CreateRoomElevations(document, sectionType, item4, drawingSettings, drawingSetReport);
					if (roomElevationGroup.Views.Count > 0)
					{
						list3.Add(roomElevationGroup);
					}
				}
				val4.Commit();
			}
			finally
			{
				((IDisposable)val4)?.Dispose();
			}
			Transaction val5 = new Transaction(document, "Codex create drawing sheets");
			try
			{
				val5.Start();
				CreateSheets(document, list, list2, list3, drawingSetReport);
				val5.Commit();
			}
			finally
			{
				((IDisposable)val5)?.Dispose();
			}
			val.Assimilate();
			return drawingSetReport.ToResult();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static object BuildDryRunReport(Document document, DrawingSettings settings, IReadOnlyList<Room> rooms, IReadOnlyList<PlanSpec> planSpecs)
	{
		int num = settings.VerticalAxes.Count + settings.HorizontalAxes.Count;
		int num2 = settings.VerticalAxes.Concat(settings.HorizontalAxes).Count((AxisSpec axis) => FindGrid(document, axis.Name) != null);
		string[] array = new string[4] { "A201 建筑南立面图", "A202 建筑东立面图", "A203 建筑北立面图", "A204 建筑西立面图" };
		int roomElevationViews = rooms.Count * 4;
		int sheets = planSpecs.Count + 1 + rooms.Count;
		return new
		{
			dryRun = true,
			proposed = new
			{
				grids = num,
				newGrids = num - num2,
				planViews = planSpecs.Count,
				newPlanViews = planSpecs.Count((PlanSpec spec) => FindViewByName(document, spec.ViewName) == null),
				planDimensions = planSpecs.Count * 4,
				buildingElevationViews = array.Length,
				newBuildingElevationViews = array.Count((string name) => FindViewByName(document, name) == null),
				roomElevationViews = roomElevationViews,
				roomsDetected = rooms.Count,
				sheets = sheets
			},
			settings = new
			{
				boundsMm = new
				{
					minX = settings.MinXmm,
					maxX = settings.MaxXmm,
					minY = settings.MinYmm,
					maxY = settings.MaxYmm,
					top = settings.TopMm
				},
				verticalAxes = settings.VerticalAxes.Select((AxisSpec axis) => new
				{
					Name = axis.Name,
					xMm = axis.ValueMm
				}),
				horizontalAxes = settings.HorizontalAxes.Select((AxisSpec axis) => new
				{
					Name = axis.Name,
					yMm = axis.ValueMm
				})
			}
		};
	}

	private static Grid CreateOrReuseGrid(Document document, string name, XYZ start, XYZ end, DrawingSetReport report)
	{
		Grid val = FindGrid(document, name);
		if (val != null)
		{
			report.ReusedGrids.Add(new
			{
				id = ((Element)val).Id.Value,
				name = ((Element)val).Name
			});
			return val;
		}
		Grid val2 = Grid.Create(document, Line.CreateBound(start, end));
		((Element)val2).Name = name;
		report.CreatedGrids.Add(new
		{
			id = ((Element)val2).Id.Value,
			name = ((Element)val2).Name
		});
		return val2;
	}

	private static ViewPlan CreateOrReusePlanView(Document document, ViewFamilyType floorPlanType, PlanSpec spec, DrawingSettings settings, DrawingSetReport report)
	{
		View? obj = FindViewByName(document, spec.ViewName);
		ViewPlan val = (ViewPlan)(object)((obj is ViewPlan) ? obj : null);
		if (val != null)
		{
			report.ReusedViews.Add(new
			{
				id = ((Element)val).Id.Value,
				name = ((Element)val).Name,
				kind = "plan"
			});
			return val;
		}
		ViewPlan val2 = ViewPlan.Create(document, ((Element)floorPlanType).Id, ((Element)spec.Level).Id);
		((Element)val2).Name = spec.ViewName;
		((View)val2).Scale = settings.PlanScale;
		TrySetPlanCrop(val2, settings);
		report.CreatedViews.Add(new
		{
			id = ((Element)val2).Id.Value,
			name = ((Element)val2).Name,
			kind = "plan"
		});
		return val2;
	}

	private static void TrySetPlanCrop(ViewPlan view, DrawingSettings settings)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Expected O, but got Unknown
		//IL_007a: Expected O, but got Unknown
		try
		{
			BoundingBoxXYZ cropBox = new BoundingBoxXYZ
			{
				Min = new XYZ(MmToFeet(settings.MinXmm - settings.GridPaddingMm), MmToFeet(settings.MinYmm - settings.GridPaddingMm), -10.0),
				Max = new XYZ(MmToFeet(settings.MaxXmm + settings.GridPaddingMm), MmToFeet(settings.MaxYmm + settings.GridPaddingMm), 10.0)
			};
			((View)view).CropBox = cropBox;
			((View)view).CropBoxActive = true;
			((View)view).CropBoxVisible = true;
		}
		catch (Exception exception)
		{
			BridgeLog.Error("Could not set plan crop box.", exception);
		}
	}

	private static void CreateGridDimensions(Document document, ViewPlan view, DrawingSettings settings, IReadOnlyDictionary<string, Grid> gridMap, DrawingSetReport report)
	{
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Expected O, but got Unknown
		//IL_010f: Expected O, but got Unknown
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_0170: Unknown result type (might be due to invalid IL or missing references)
		//IL_017a: Expected O, but got Unknown
		//IL_017a: Expected O, but got Unknown
		//IL_01b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ea: Expected O, but got Unknown
		//IL_01ea: Expected O, but got Unknown
		//IL_0220: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0255: Expected O, but got Unknown
		//IL_0255: Expected O, but got Unknown
		List<Grid> grids = (from axis in settings.VerticalAxes
			where gridMap.ContainsKey(axis.Name)
			orderby axis.ValueMm
			select gridMap[axis.Name]).ToList();
		List<Grid> grids2 = (from axis in settings.HorizontalAxes
			where gridMap.ContainsKey(axis.Name)
			orderby axis.ValueMm
			select gridMap[axis.Name]).ToList();
		CreateDimensionSafe(document, (View)(object)view, Line.CreateBound(new XYZ(MmToFeet(settings.MinXmm), MmToFeet(settings.MinYmm - settings.DimensionOffsetMm), 0.0), new XYZ(MmToFeet(settings.MaxXmm), MmToFeet(settings.MinYmm - settings.DimensionOffsetMm), 0.0)), grids, "axis-x-segments", report);
		CreateDimensionSafe(document, (View)(object)view, Line.CreateBound(new XYZ(MmToFeet(settings.MinXmm), MmToFeet(settings.MinYmm - settings.OverallDimensionOffsetMm), 0.0), new XYZ(MmToFeet(settings.MaxXmm), MmToFeet(settings.MinYmm - settings.OverallDimensionOffsetMm), 0.0)), TakeFirstLast(grids), "axis-x-overall", report);
		CreateDimensionSafe(document, (View)(object)view, Line.CreateBound(new XYZ(MmToFeet(settings.MinXmm - settings.DimensionOffsetMm), MmToFeet(settings.MinYmm), 0.0), new XYZ(MmToFeet(settings.MinXmm - settings.DimensionOffsetMm), MmToFeet(settings.MaxYmm), 0.0)), grids2, "axis-y-segments", report);
		CreateDimensionSafe(document, (View)(object)view, Line.CreateBound(new XYZ(MmToFeet(settings.MinXmm - settings.OverallDimensionOffsetMm), MmToFeet(settings.MinYmm), 0.0), new XYZ(MmToFeet(settings.MinXmm - settings.OverallDimensionOffsetMm), MmToFeet(settings.MaxYmm), 0.0)), TakeFirstLast(grids2), "axis-y-overall", report);
	}

	private static IReadOnlyList<Grid> TakeFirstLast(IReadOnlyList<Grid> grids)
	{
		IReadOnlyList<Grid> result;
		if (grids.Count >= 2)
		{
			Grid[] obj = new Grid[2]
			{
				grids[0],
				default(Grid)
			};
			obj[1] = grids[grids.Count - 1];
			IReadOnlyList<Grid> readOnlyList = (IReadOnlyList<Grid>)(object)obj;
			result = readOnlyList;
		}
		else
		{
			result = grids;
		}
		return result;
	}

	private static void CreateDimensionSafe(Document document, View view, Line line, IReadOnlyList<Grid> grids, string label, DrawingSetReport report)
	{
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Expected O, but got Unknown
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Expected O, but got Unknown
		if (grids.Count < 2)
		{
			report.Warnings.Add($"Skipped {label} dimension in {((Element)view).Name}: fewer than two grids.");
			return;
		}
		try
		{
			ReferenceArray val = new ReferenceArray();
			foreach (Grid grid in grids)
			{
				val.Append(new Reference((Element)(object)grid));
			}
			Dimension val2 = document.Create.NewDimension(view, line, val);
			report.CreatedDimensions.Add(new
			{
				id = ((Element)val2).Id.Value,
				view = ((Element)view).Name,
				label = label
			});
		}
		catch (Exception ex)
		{
			report.Warnings.Add($"Could not create {label} dimension in {((Element)view).Name}: {ex.Message}");
			BridgeLog.Error("Could not create dimension " + label + ".", ex);
		}
	}

	private static IReadOnlyList<ViewSection> CreateBuildingElevations(Document document, ViewFamilyType sectionType, DrawingSettings settings, DrawingSetReport report)
	{
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Expected O, but got Unknown
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0157: Expected O, but got Unknown
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Expected O, but got Unknown
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f2: Expected O, but got Unknown
		double value = (settings.MinXmm + settings.MaxXmm) / 2.0;
		double value2 = (settings.MinYmm + settings.MaxYmm) / 2.0;
		double widthMm = settings.MaxXmm - settings.MinXmm + settings.ElevationPaddingMm * 2.0;
		double widthMm2 = settings.MaxYmm - settings.MinYmm + settings.ElevationPaddingMm * 2.0;
		double depthMm = settings.MaxXmm - settings.MinXmm + settings.ElevationPaddingMm * 3.0;
		double depthMm2 = settings.MaxYmm - settings.MinYmm + settings.ElevationPaddingMm * 3.0;
		double elevationOffsetMm = settings.ElevationOffsetMm;
		return (IReadOnlyList<ViewSection>)(object)new ViewSection[4]
		{
			CreateOrReuseSectionView(document, sectionType, "A201 建筑南立面图", new XYZ(MmToFeet(value), MmToFeet(settings.MinYmm - elevationOffsetMm), 0.0), XYZ.BasisY, widthMm, settings.TopMm, depthMm2, settings.ElevationScale, "building-elevation", report),
			CreateOrReuseSectionView(document, sectionType, "A202 建筑东立面图", new XYZ(MmToFeet(settings.MaxXmm + elevationOffsetMm), MmToFeet(value2), 0.0), -XYZ.BasisX, widthMm2, settings.TopMm, depthMm, settings.ElevationScale, "building-elevation", report),
			CreateOrReuseSectionView(document, sectionType, "A203 建筑北立面图", new XYZ(MmToFeet(value), MmToFeet(settings.MaxYmm + elevationOffsetMm), 0.0), -XYZ.BasisY, widthMm, settings.TopMm, depthMm2, settings.ElevationScale, "building-elevation", report),
			CreateOrReuseSectionView(document, sectionType, "A204 建筑西立面图", new XYZ(MmToFeet(settings.MinXmm - elevationOffsetMm), MmToFeet(value2), 0.0), XYZ.BasisX, widthMm2, settings.TopMm, depthMm, settings.ElevationScale, "building-elevation", report)
		};
	}

	private static RoomElevationGroup CreateRoomElevations(Document document, ViewFamilyType sectionType, Room room, DrawingSettings settings, DrawingSetReport report)
	{
		//IL_01b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01da: Expected O, but got Unknown
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0234: Expected O, but got Unknown
		//IL_0267: Unknown result type (might be due to invalid IL or missing references)
		//IL_0293: Expected O, but got Unknown
		//IL_02c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f2: Expected O, but got Unknown
		BoundingBoxXYZ val = room.get_BoundingBox(null);
		RoomElevationGroup roomElevationGroup = new RoomElevationGroup(room);
		if (val == null)
		{
			report.Warnings.Add("Skipped room elevations for " + RoomLabel(room) + ": no bounding box.");
			return roomElevationGroup;
		}
		Element element = document.GetElement(((Element)room).LevelId);
		Level val2 = (Level)(object)((element is Level) ? element : null);
		double num = ((val2 != null) ? val2.Elevation : 0.0);
		double value = ResolveRoomHeight(document, val2, settings);
		double num2 = FeetToMm(val.Min.X);
		double num3 = FeetToMm(val.Max.X);
		double num4 = FeetToMm(val.Min.Y);
		double num5 = FeetToMm(val.Max.Y);
		double widthMm = Math.Max(1000.0, num3 - num2 + settings.RoomElevationPaddingMm * 2.0);
		double widthMm2 = Math.Max(1000.0, num5 - num4 + settings.RoomElevationPaddingMm * 2.0);
		double depthMm = Math.Max(1000.0, num3 - num2 + settings.RoomElevationPaddingMm);
		double depthMm2 = Math.Max(1000.0, num5 - num4 + settings.RoomElevationPaddingMm);
		double value2 = (num2 + num3) / 2.0;
		double value3 = (num4 + num5) / 2.0;
		string text = "R" + ((SpatialElement)room).Number + "-" + CleanName(((Element)room).Name);
		roomElevationGroup.Views.Add(CreateOrReuseSectionView(document, sectionType, text + "-北立面", new XYZ(MmToFeet(value2), MmToFeet(num4 + 150.0), num), XYZ.BasisY, widthMm, FeetToMm(value), depthMm2, settings.RoomElevationScale, "room-elevation", report));
		roomElevationGroup.Views.Add(CreateOrReuseSectionView(document, sectionType, text + "-东立面", new XYZ(MmToFeet(num2 + 150.0), MmToFeet(value3), num), XYZ.BasisX, widthMm2, FeetToMm(value), depthMm, settings.RoomElevationScale, "room-elevation", report));
		roomElevationGroup.Views.Add(CreateOrReuseSectionView(document, sectionType, text + "-南立面", new XYZ(MmToFeet(value2), MmToFeet(num5 - 150.0), num), -XYZ.BasisY, widthMm, FeetToMm(value), depthMm2, settings.RoomElevationScale, "room-elevation", report));
		roomElevationGroup.Views.Add(CreateOrReuseSectionView(document, sectionType, text + "-西立面", new XYZ(MmToFeet(num3 - 150.0), MmToFeet(value3), num), -XYZ.BasisX, widthMm2, FeetToMm(value), depthMm, settings.RoomElevationScale, "room-elevation", report));
		return roomElevationGroup;
	}

	private static ViewSection CreateOrReuseSectionView(Document document, ViewFamilyType sectionType, string viewName, XYZ origin, XYZ viewDirection, double widthMm, double heightMm, double depthMm, int scale, string kind, DrawingSetReport report)
	{
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Expected O, but got Unknown
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected O, but got Unknown
		//IL_00f3: Expected O, but got Unknown
		View? obj = FindViewByName(document, viewName);
		ViewSection val = (ViewSection)(object)((obj is ViewSection) ? obj : null);
		if (val != null)
		{
			report.ReusedViews.Add(new
			{
				id = ((Element)val).Id.Value,
				name = ((Element)val).Name,
				kind = kind
			});
			return val;
		}
		XYZ val2 = viewDirection.Normalize();
		XYZ basisX = val2.CrossProduct(XYZ.BasisZ).Normalize();
		Transform identity = Transform.Identity;
		identity.Origin = origin;
		identity.BasisX = basisX;
		identity.BasisY = XYZ.BasisZ;
		identity.BasisZ = val2;
		BoundingBoxXYZ val3 = new BoundingBoxXYZ
		{
			Transform = identity,
			Min = new XYZ((0.0 - MmToFeet(widthMm)) / 2.0, 0.0, 0.0),
			Max = new XYZ(MmToFeet(widthMm) / 2.0, MmToFeet(heightMm), MmToFeet(depthMm))
		};
		ViewSection val4 = ViewSection.CreateSection(document, ((Element)sectionType).Id, val3);
		((Element)val4).Name = CreateUniqueViewName(document, viewName);
		((View)val4).Scale = scale;
		report.CreatedViews.Add(new
		{
			id = ((Element)val4).Id.Value,
			name = ((Element)val4).Name,
			kind = kind
		});
		return val4;
	}

	private static void CreateSheets(Document document, IReadOnlyList<ViewPlan> planViews, IReadOnlyList<ViewSection> buildingElevationViews, IReadOnlyList<RoomElevationGroup> roomElevationGroups, DrawingSetReport report)
	{
		ElementId titleBlockId = ResolveTitleBlockId(document);
		for (int i = 0; i < planViews.Count; i++)
		{
			string sheetNumber = $"A10{i + 1}";
			string name = ((Element)planViews[i]).Name;
			ViewSheet sheet = CreateOrReuseSheet(document, titleBlockId, sheetNumber, name, report);
			PlaceViewOnSheet(document, sheet, (View)(object)planViews[i], SheetPoint(0.0, 0.0), report);
		}
		ViewSheet sheet2 = CreateOrReuseSheet(document, titleBlockId, "A201", "建筑东南西北立面图", report);
		PlaceViewGridOnSheet(document, sheet2, buildingElevationViews.Cast<View>().ToList(), report);
		for (int j = 0; j < roomElevationGroups.Count; j++)
		{
			RoomElevationGroup roomElevationGroup = roomElevationGroups[j];
			ViewSheet sheet3 = CreateOrReuseSheet(document, titleBlockId, $"A{301 + j}", RoomLabel(roomElevationGroup.Room) + " 四向室内立面图", report);
			PlaceViewGridOnSheet(document, sheet3, roomElevationGroup.Views.Cast<View>().ToList(), report);
		}
	}

	private static ViewSheet CreateOrReuseSheet(Document document, ElementId titleBlockId, string sheetNumber, string sheetName, DrawingSetReport report)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		ViewSheet val = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(ViewSheet))).Cast<ViewSheet>().FirstOrDefault((ViewSheet sheet) => string.Equals(sheet.SheetNumber, sheetNumber, StringComparison.OrdinalIgnoreCase));
		if (val != null)
		{
			report.ReusedSheets.Add(new
			{
				id = ((Element)val).Id.Value,
				number = val.SheetNumber,
				name = ((Element)val).Name
			});
			return val;
		}
		ViewSheet val2 = ViewSheet.Create(document, titleBlockId);
		val2.SheetNumber = sheetNumber;
		((Element)val2).Name = sheetName;
		report.CreatedSheets.Add(new
		{
			id = ((Element)val2).Id.Value,
			number = val2.SheetNumber,
			name = ((Element)val2).Name
		});
		return val2;
	}

	private static void PlaceViewGridOnSheet(Document document, ViewSheet sheet, IReadOnlyList<View> views, DrawingSetReport report)
	{
		XYZ[] array = (XYZ[])(object)new XYZ[4]
		{
			SheetPoint(-230.0, 120.0),
			SheetPoint(230.0, 120.0),
			SheetPoint(-230.0, -120.0),
			SheetPoint(230.0, -120.0)
		};
		for (int i = 0; i < views.Count && i < array.Length; i++)
		{
			PlaceViewOnSheet(document, sheet, views[i], array[i], report);
		}
	}

	private static void PlaceViewOnSheet(Document document, ViewSheet sheet, View view, XYZ point, DrawingSetReport report)
	{
		try
		{
			if (!Viewport.CanAddViewToSheet(document, ((Element)sheet).Id, ((Element)view).Id))
			{
				report.Warnings.Add($"Skipped viewport for {((Element)view).Name} on sheet {sheet.SheetNumber}: view cannot be placed.");
			}
			else
			{
				Viewport val = Viewport.Create(document, ((Element)sheet).Id, ((Element)view).Id, point);
				report.CreatedViewports.Add(new
				{
					id = ((Element)val).Id.Value,
					sheet = sheet.SheetNumber,
					view = ((Element)view).Name
				});
			}
		}
		catch (Exception ex)
		{
			report.Warnings.Add($"Could not place {((Element)view).Name} on sheet {sheet.SheetNumber}: {ex.Message}");
			BridgeLog.Error("Could not place viewport.", ex);
		}
	}

	private static IReadOnlyList<PlanSpec> ResolvePlanSpecs(Document document, DrawingSettings settings)
	{
		List<Level> list = (from name in settings.LevelNames
			select ResolveLevel(document, name) into level
			orderby level.Elevation
			select level).ToList();
		List<PlanSpec> list2 = new List<PlanSpec>();
		for (int num = 0; num < list.Count; num++)
		{
			string text = $"A10{num + 1}";
			string text2;
			if (((Element)list[num]).Name.Contains("屋顶", StringComparison.OrdinalIgnoreCase))
			{
				text2 = text + " 屋顶平面图";
			}
			else
			{
				if (1 == 0)
				{
				}
				string text3 = num switch
				{
					0 => "A101 一层平面图", 
					1 => "A102 二层平面图", 
					2 => "A103 三层平面图", 
					_ => text + " " + ((Element)list[num]).Name + " 平面图", 
				};
				if (1 == 0)
				{
				}
				text2 = text3;
			}
			string viewName = text2;
			list2.Add(new PlanSpec(list[num], viewName));
		}
		return list2;
	}

	private static IReadOnlyList<Room> CollectRooms(Document document)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		return (from Room room in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(SpatialElement)).OfCategory((BuiltInCategory)(-2000160))
			where ((SpatialElement)room).Area > 0.0
			select room).OrderBy(delegate(Room room)
		{
			Element element = document.GetElement(((Element)room).LevelId);
			Element obj = ((element is Level) ? element : null);
			return (obj != null) ? ((Level)obj).Elevation : 0.0;
		}).ThenBy<Room, string>((Room room) => ((SpatialElement)room).Number, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static double ResolveRoomHeight(Document document, Level? level, DrawingSettings settings)
	{
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		if (level == null)
		{
			return MmToFeet(settings.SecondFloorHeightMm);
		}
		Level val = (from Level candidate in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Level))
			where candidate.Elevation > level.Elevation
			orderby candidate.Elevation
			select candidate).FirstOrDefault();
		return (val == null) ? MmToFeet(settings.SecondFloorHeightMm) : (val.Elevation - level.Elevation);
	}

	private static ViewFamilyType ResolveViewFamilyType(Document document, ViewFamily viewFamily)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		return ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))).Cast<ViewFamilyType>().FirstOrDefault((ViewFamilyType type) => type.ViewFamily == viewFamily) ?? throw new InvalidOperationException($"No ViewFamilyType found for {viewFamily}.");
	}

	private static Level ResolveLevel(Document document, string levelName)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		return ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Level))).Cast<Level>().FirstOrDefault((Level level) => string.Equals(((Element)level).Name, levelName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Level '" + levelName + "' was not found.");
	}

	private static Grid? FindGrid(Document document, string name)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		return ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Grid))).Cast<Grid>().FirstOrDefault((Grid grid) => string.Equals(((Element)grid).Name, name, StringComparison.OrdinalIgnoreCase));
	}

	private static View? FindViewByName(Document document, string name)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		return ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(View))).Cast<View>().FirstOrDefault((View view) => !view.IsTemplate && string.Equals(((Element)view).Name, name, StringComparison.OrdinalIgnoreCase));
	}

	private static string CreateUniqueViewName(Document document, string desiredName)
	{
		if (FindViewByName(document, desiredName) == null)
		{
			return desiredName;
		}
		for (int i = 2; i < 1000; i++)
		{
			string text = $"{desiredName} ({i})";
			if (FindViewByName(document, text) == null)
			{
				return text;
			}
		}
		throw new InvalidOperationException("Could not create a unique view name for '" + desiredName + "'.");
	}

	private static ElementId ResolveTitleBlockId(Document document)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		return (from FamilySymbol symbol in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).OfCategory((BuiltInCategory)(-2000280))
			select ((Element)symbol).Id).FirstOrDefault(ElementId.InvalidElementId);
	}

	private static string RoomLabel(Room room)
	{
		return ((SpatialElement)room).Number + "-" + CleanName(((Element)room).Name);
	}

	private static string CleanName(string value)
	{
		char[] source = new char[11]
		{
			'{', '}', '[', ']', '|', ';', '<', '>', '?', '`',
			'~'
		};
		string text = source.Aggregate(value, (string current, char ch) => current.Replace(ch, '-')).Trim();
		return string.IsNullOrWhiteSpace(text) ? "未命名房间" : text;
	}

	private static XYZ SheetPoint(double xMm, double yMm)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		return new XYZ(MmToFeet(xMm), MmToFeet(yMm), 0.0);
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
