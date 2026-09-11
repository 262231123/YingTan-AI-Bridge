using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using View = Autodesk.Revit.DB.View;

namespace RevitCodexBridge.Addin;

internal static class CommandExecutor
{
	private sealed record BatchOperation(string Id, string Command, string? Description, JsonElement Payload, bool IsMutation, bool DryRun);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static object? Execute(UIApplication app, JsonElement payload)
	{
		DesignAgentTools.CheckDocument(app, payload);
		string text = payload.GetRequiredString("command").ToLowerInvariant();
		if (1 == 0)
		{
		}
		object result = text switch
		{
			"run_batch" => RunBatch(app, payload),
			"get_model_context" => DesignAgentTools.Context(app),
			"prepare_revit_script" => RevitScriptHost.Prepare(GetDocument(app), payload),
			"query_revit_script" => RevitScriptHost.Query(GetDocument(app), payload),
			"execute_revit_script" => RevitScriptHost.Execute(GetDocument(app), payload),
			"list_grids" => SteelPlatformTools.ListGrids(GetDocument(app)),
			"resolve_grid_region" => SteelPlatformTools.ResolveRegion(GetDocument(app), payload),
			"list_structure_types" => SteelPlatformTools.Types(GetDocument(app), payload),
			"preview_steel_platform" => SteelPlatformTools.PreviewPlatform(GetDocument(app), payload),
			"create_steel_platform" => SteelPlatformTools.Create(GetDocument(app), payload),
			"find_elements" => DesignAgentTools.Find(app, payload),
			"list_warnings" => DesignAgentTools.Warnings(GetDocument(app), payload),
			"create_room_layout" => DesignAgentTools.RoomLayout(GetDocument(app), payload),
			"get_active_document" => GetActiveDocument(app), 
			"list_levels" => ListLevels(GetDocument(app)), 
			"list_wall_types" => ListWallTypes(GetDocument(app)), 
			"list_family_symbols" => ListFamilySymbols(GetDocument(app), payload), 
			"count_elements" => CountElements(GetDocument(app), payload), 
			"analyze_walls" => AnalyzeWalls(GetDocument(app)), 
			"get_selection" => GetSelection(app), 
			"get_element" => GetElementDetails(GetDocument(app), payload), 
			"list_views" => ListViews(GetDocument(app), payload), 
			"list_sheets" => ListSheets(GetDocument(app)), 
			"list_schedules" => ListSchedules(GetDocument(app)), 
			"show_elements" => ShowElements(app, payload), 
			"finish_toolkit_info" => ExecuteFinishToolkitInfo(app, payload), 
			"finish_toolkit_run" => ExecuteFinishToolkitRun(app, payload), 
			"set_parameter" => SetParameter(GetDocument(app), payload), 
			"create_wall" => CreateWall(GetDocument(app), payload), 
			"place_door" => PlaceHostedFamilyInstance(GetDocument(app), payload, (BuiltInCategory)(-2000023), "door"), 
			"place_window" => PlaceHostedFamilyInstance(GetDocument(app), payload, (BuiltInCategory)(-2000014), "window"), 
			"create_room" => CreateRoom(GetDocument(app), payload), 
			"create_compound_wall_type" => ExecuteBulkMutation(GetDocument(app), payload, "Create compound wall type", "This will create a Revit basic wall type with the requested layers and may replace matching wall instances.", CompoundWallTypeAutomation.Create), 
			"create_drawing_set" => ExecuteBulkMutation(GetDocument(app), payload, "Create drawing set", "This will create or reuse grids, plan views, dimensions, elevation views, sheets, and viewports.", DrawingSetBuilder.Create), 
			"create_energy_cube_model" => ExecuteBulkMutation(GetDocument(app), payload, "Create Energy Cube model", "This will create levels, floors, walls, doors, windows, rooms, generic model objects, and site elements.", EnergyCubeProjectBuilder.Create), 
			_ => throw new InvalidOperationException("Unsupported bridge command '" + text + "'."), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static object RunBatch(UIApplication app, JsonElement payload)
	{
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d4: Invalid comparison between Unknown and I4
		//IL_01e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Expected O, but got Unknown
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
		Document document = GetDocument(app);
		bool optionalBoolean = payload.GetOptionalBoolean("dryRun", defaultValue: true);
		bool optionalBoolean2 = payload.GetOptionalBoolean("atomic", defaultValue: true);
		bool optionalBoolean3 = payload.GetOptionalBoolean("continueOnError", defaultValue: false);
		IReadOnlyList<BatchOperation> readOnlyList = ReadBatchOperations(payload, optionalBoolean);
		int num = readOnlyList.Count((BatchOperation operation) => operation.IsMutation && !operation.DryRun);
		List<object> list = new List<object>();
		int num2 = 0;
		bool rolledBack = false;
		if (readOnlyList.Count == 0)
		{
			throw new InvalidOperationException("run_batch requires at least one operation.");
		}
		if (num > 0)
		{
			RequireWriteApproval(payload, "Run Revit batch", BuildBatchApprovalSummary(readOnlyList, num, optionalBoolean2));
		}
		TransactionGroup val = null;
		try
		{
			if (num > 0 && optionalBoolean2)
			{
				val = new TransactionGroup(document, "Codex batch");
				val.Start();
			}
			foreach (BatchOperation item in readOnlyList)
			{
				try
				{
					object result = Execute(app, item.Payload);
					list.Add(new
					{
						Id = item.Id,
						Command = item.Command,
						Description = item.Description,
						DryRun = item.DryRun,
						ok = true,
						result = result
					});
				}
				catch (OperationCanceledException) { throw; }
				catch (Exception ex)
				{
					num2++;
					list.Add(new
					{
						Id = item.Id,
						Command = item.Command,
						Description = item.Description,
						DryRun = item.DryRun,
						ok = false,
						error = ex.Message
					});
					if (!optionalBoolean3)
					{
						break;
					}
				}
			}
			if (val != null)
			{
				if (num2 == 0)
				{
					if (val.Assimilate() != TransactionStatus.Committed)
						throw new InvalidOperationException("Revit did not commit the batch transaction group.");
				}
				else
				{
					val.RollBack();
					rolledBack = true;
				}
			}
		}
		catch
		{
			if (val != null && (int)val.GetStatus() == 1)
			{
				val.RollBack();
				rolledBack = true;
			}
			throw;
		}
		finally
		{
			if (val != null)
			{
				val.Dispose();
			}
		}
		return new
		{
			dryRun = optionalBoolean,
			atomic = optionalBoolean2,
			continueOnError = optionalBoolean3,
			requested = readOnlyList.Count,
			executed = list.Count,
			writableMutations = num,
			failures = num2,
			rolledBack = rolledBack,
			committed = (num > 0 && num2 == 0 && !optionalBoolean),
			results = list
		};
	}

	private static Document GetDocument(UIApplication app)
	{
		UIDocument activeUIDocument = app.ActiveUIDocument;
		return ((activeUIDocument != null) ? activeUIDocument.Document : null) ?? throw new InvalidOperationException("No active Revit document is open.");
	}

	private static object GetActiveDocument(UIApplication app)
	{
		UIDocument activeUIDocument = app.ActiveUIDocument;
		Document val = ((activeUIDocument != null) ? activeUIDocument.Document : null);
		if (val == null)
		{
			return new
			{
				hasDocument = false
			};
		}
		string title = val.Title;
		string pathName = val.PathName;
		bool isFamilyDocument = val.IsFamilyDocument;
		bool isWorkshared = val.IsWorkshared;
		object activeView;
		if (activeUIDocument == null)
		{
			activeView = null;
		}
		else
		{
			View activeView2 = activeUIDocument.ActiveView;
			activeView = ((activeView2 != null) ? ((Element)activeView2).Name : null);
		}
		return new
		{
			hasDocument = true,
			title = title,
			path = pathName,
			isFamilyDocument = isFamilyDocument,
			isWorkshared = isWorkshared,
			activeView = (string)activeView
		};
	}

	private static object ListLevels(Document document)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		var list = (from Level level in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Level))
			orderby level.Elevation
			select new
			{
				id = ((Element)level).Id.Value,
				Name = ((Element)level).Name,
				elevationFeet = level.Elevation,
				elevationMm = FeetToMillimeters(level.Elevation)
			}).ToList();
		return new
		{
			count = list.Count,
			levels = list
		};
	}

	private static object ListWallTypes(Document document)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		var list = (from WallType type in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(WallType))
			orderby ((Element)type).Name
			select new
			{
				id = ((Element)type).Id.Value,
				Name = ((Element)type).Name,
				familyName = ((ElementType)type).FamilyName,
				kind = ((object)type.Kind/*cast due to constrained. prefix*/).ToString()
			}).ToList();
		return new
		{
			count = list.Count,
			wallTypes = list
		};
	}

	private static object ListFamilySymbols(Document document, JsonElement payload)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		string requiredString = payload.GetRequiredString("category");
		if (!Enum.TryParse<BuiltInCategory>(requiredString, true, out BuiltInCategory result))
		{
			throw new InvalidOperationException("Unknown BuiltInCategory '" + requiredString + "'.");
		}
		var list = (from FamilySymbol symbol in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).OfCategory(result)
			orderby ((ElementType)symbol).FamilyName, ((Element)symbol).Name
			select symbol).Select(delegate(FamilySymbol symbol)
		{
			long value = ((Element)symbol).Id.Value;
			string familyName = ((ElementType)symbol).FamilyName;
			string name = ((Element)symbol).Name;
			Category category = ((Element)symbol).Category;
			return new
			{
				id = value,
				familyName = familyName,
				typeName = name,
				category = ((category != null) ? category.Name : null)
			};
		}).ToList();
		return new
		{
			category = requiredString,
			count = list.Count,
			symbols = list
		};
	}

	private static object CountElements(Document document, JsonElement payload)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Invalid comparison between Unknown and I8
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		FilteredElementCollector val = new FilteredElementCollector(document).WhereElementIsNotElementType();
		string optionalString = payload.GetOptionalString("category");
		if (!string.IsNullOrWhiteSpace(optionalString))
		{
			if (!Enum.TryParse<BuiltInCategory>(optionalString, true, out BuiltInCategory result))
			{
				throw new InvalidOperationException("Unknown BuiltInCategory '" + optionalString + "'.");
			}
			if ((long)result == -2000011)
			{
				return AnalyzeWalls(document);
			}
			val = val.OfCategory(result);
		}
		return new
		{
			category = optionalString,
			count = val.GetElementCount()
		};
	}

	private static object AnalyzeWalls(Document document)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		List<Wall> list = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Wall))).Cast<Wall>().ToList();
		int stackedWallContainers = list.Count((Wall wall) => wall.IsStackedWall);
		int stackedWallMembers = list.Count((Wall wall) => wall.IsStackedWallMember);
		int num = list.Count((Wall wall) => !wall.IsStackedWall);
		var byKind = (from @group in list.GroupBy(delegate(Wall wall)
			{
				//IL_001d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0022: Unknown result type (might be due to invalid IL or missing references)
				Element element = document.GetElement(((Element)wall).GetTypeId());
				Element obj = ((element is WallType) ? element : null);
				return ((obj != null) ? ((object)((WallType)obj).Kind/*cast due to constrained. prefix*/).ToString() : null) ?? "Unknown";
			})
			orderby @group.Key
			select new
			{
				kind = @group.Key,
				count = @group.Count()
			}).ToList();
		var byType = (from @group in list.GroupBy(delegate(Wall wall)
			{
				Element element = document.GetElement(((Element)wall).GetTypeId());
				Element obj = ((element is WallType) ? element : null);
				return ((obj != null) ? obj.Name : null) ?? "<未知墙类型>";
			})
			orderby @group.Count() descending, @group.Key
			select new
			{
				typeName = @group.Key,
				count = @group.Count()
			}).ToList();
		List<object> list2 = GetSchedules(document, (BuiltInCategory)(-2000011)).ToList();
		return new
		{
			category = "OST_Walls",
			count = num,
			allWallInstances = list.Count,
			scheduleComparableCount = num,
			stackedWallContainers = stackedWallContainers,
			stackedWallMembers = stackedWallMembers,
			excludedFromComparableCount = "仅排除叠层墙容器；叠层墙成员仍计入",
			byKind = byKind,
			byType = byType,
			wallSchedules = list2,
			reconciliation = ((list2.Count == 0) ? "项目中未找到墙明细表。count 为排除叠层墙容器后的模型墙实例数。" : "请优先将 count 与 isItemized=true 的墙明细表 visibleInstances 比较；若明细表包含过滤器或未逐项列举，表格行数不等于模型实例数。")
		};
	}

	private static object GetSelection(UIApplication app)
	{
		UIDocument val = app.ActiveUIDocument ?? throw new InvalidOperationException("No active Revit document is open.");
		Document document = val.Document;
		List<object> list = (from element in ((IEnumerable<ElementId>)val.Selection.GetElementIds()).Select((Func<ElementId, Element>)document.GetElement)
			where element != null
			select DescribeElement(document, element)).ToList();
		return new
		{
			count = list.Count,
			elements = list
		};
	}

	private static object GetElementDetails(Document document, JsonElement payload)
	{
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		long requiredInt = payload.GetRequiredInt64("elementId");
		Element val = document.GetElement(new ElementId(requiredInt)) ?? throw new InvalidOperationException($"Element {requiredInt} was not found.");
		var list = (from Parameter parameter in (IEnumerable)val.Parameters
			where parameter.Definition != null
			select new
			{
				name = parameter.Definition.Name,
				storageType = ((object)parameter.StorageType/*cast due to constrained. prefix*/).ToString(),
				value = ReadParameterText(document, parameter),
				isReadOnly = ((APIObject)parameter).IsReadOnly
			} into parameter
			where !string.IsNullOrWhiteSpace(parameter.value)
			orderby parameter.name
			select parameter).Take(200).ToList();
		return new
		{
			element = DescribeElement(document, val),
			parameterCount = list.Count,
			parameters = list
		};
	}

	private static object ListViews(Document document, JsonElement payload)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		string requestedType = payload.GetOptionalString("viewType");
		var list = (from View view in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(View))
			where !view.IsTemplate && !(view is ViewSheet) && !(view is ViewSchedule)
			where string.IsNullOrWhiteSpace(requestedType) || ((object)view.ViewType/*cast due to constrained. prefix*/).ToString().Equals(requestedType, StringComparison.OrdinalIgnoreCase)
			orderby view.ViewType, ((Element)view).Name
			select new
			{
				id = ((Element)view).Id.Value,
				Name = ((Element)view).Name,
				viewType = ((object)view.ViewType/*cast due to constrained. prefix*/).ToString()
			}).ToList();
		return new
		{
			viewType = requestedType,
			count = list.Count,
			views = list
		};
	}

	private static object ListSheets(Document document)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		var list = (from ViewSheet sheet in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(ViewSheet))
			orderby sheet.SheetNumber
			select new
			{
				id = ((Element)sheet).Id.Value,
				number = sheet.SheetNumber,
				name = ((Element)sheet).Name,
				placedViewCount = sheet.GetAllPlacedViews().Count
			}).ToList();
		return new
		{
			count = list.Count,
			sheets = list
		};
	}

	private static object ListSchedules(Document document)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		List<object> list = (from ViewSchedule schedule in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(ViewSchedule))
			where !((View)schedule).IsTemplate
			orderby ((Element)schedule).Name
			select DescribeSchedule(document, schedule)).ToList();
		return new
		{
			count = list.Count,
			schedules = list
		};
	}

	private static object ShowElements(UIApplication app, JsonElement payload)
	{
		UIDocument uiDocument = app.ActiveUIDocument ?? throw new InvalidOperationException("No active Revit document is open.");
		if (!payload.TryGetProperty("elementIds", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidOperationException("show_elements requires an elementIds array.");
		}
		List<ElementId> list = (from id in ((IEnumerable<JsonElement>)value.EnumerateArray()).Select((Func<JsonElement, ElementId>)((JsonElement item) => new ElementId(item.GetInt64())))
			where uiDocument.Document.GetElement(id) != null
			select id).DistinctBy((ElementId id) => id.Value).ToList();
		if (list.Count == 0)
		{
			throw new InvalidOperationException("No valid Revit element IDs were provided.");
		}
		uiDocument.Selection.SetElementIds((ICollection<ElementId>)list);
		uiDocument.ShowElements((ICollection<ElementId>)list);
		return new
		{
			selectedAndShown = list.Count,
			elementIds = list.Select((ElementId id) => id.Value).ToList()
		};
	}

	private static object ExecuteFinishToolkitInfo(UIApplication app, JsonElement payload)
	{
		string text = payload.GetOptionalString("action")?.ToLowerInvariant() ?? "capabilities";
		bool flag;
		switch (text)
		{
		case "status":
			return FinishPartsToolkitBridge.GetStatus();
		case "capabilities":
		case "readiness":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new InvalidOperationException("finish_toolkit_info only supports status, capabilities, or readiness.");
		}
		return FinishPartsToolkitBridge.Execute(app, text);
	}

	private static object ExecuteFinishToolkitRun(UIApplication app, JsonElement payload)
	{
		string text = payload.GetRequiredString("action").ToLowerInvariant();
		if (payload.GetOptionalBoolean("dryRun", defaultValue: true))
		{
			return new
			{
				dryRun = true,
				action = text,
				toolkit = FinishPartsToolkitBridge.GetStatus(),
				nextStep = "预演通过。确认执行后将调用呆猫工作室精装插件；复杂任务会打开对应配置面板。"
			};
		}
		RequireWriteApproval(payload, "调用呆猫工作室精装 Skill", "Action: " + text + Environment.NewLine + "复杂动作可能打开精装配置面板，直接动作可能修改当前 Revit 模型。");
		return FinishPartsToolkitBridge.Execute(app, text);
	}

	private static IEnumerable<object> GetSchedules(Document document, BuiltInCategory category)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		return from ViewSchedule schedule in (IEnumerable)new FilteredElementCollector(document).OfClass(typeof(ViewSchedule))
			where !((View)schedule).IsTemplate && schedule.Definition.CategoryId.Value == (long)category
			orderby ((Element)schedule).Name
			select DescribeSchedule(document, schedule);
	}

	private static object DescribeSchedule(Document document, ViewSchedule schedule)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		ScheduleDefinition definition = schedule.Definition;
		int elementCount = new FilteredElementCollector(document, ((Element)schedule).Id).WhereElementIsNotElementType().GetElementCount();
		int numberOfRows = schedule.GetTableData().GetSectionData((SectionType)1).NumberOfRows;
		return new
		{
			id = ((Element)schedule).Id.Value,
			Name = ((Element)schedule).Name,
			categoryId = definition.CategoryId.Value,
			isItemized = definition.IsItemized,
			filterCount = definition.GetFilterCount(),
			sortGroupFieldCount = definition.GetSortGroupFieldCount(),
			visibleInstances = elementCount,
			bodyRows = numberOfRows
		};
	}

	private static object DescribeElement(Document document, Element element)
	{
		Element element2 = document.GetElement(element.GetTypeId());
		object obj;
		if (!(element.LevelId == ElementId.InvalidElementId))
		{
			Element element3 = document.GetElement(element.LevelId);
			obj = ((element3 is Level) ? element3 : null);
		}
		else
		{
			obj = null;
		}
		Level val = (Level)obj;
		long value = element.Id.Value;
		string name = element.Name;
		Category category = element.Category;
		return new
		{
			id = value,
			name = name,
			category = ((category != null) ? category.Name : null),
			typeId = ((element2 != null) ? new long?(element2.Id.Value) : ((long?)null)),
			typeName = ((element2 != null) ? element2.Name : null),
			level = ((val != null) ? ((Element)val).Name : null)
		};
	}

	private static string? ReadParameterText(Document document, Parameter parameter)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Expected I4, but got Unknown
		try
		{
			string text = parameter.AsValueString();
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
            return parameter.StorageType switch
            {
                StorageType.String => parameter.AsString(),
                StorageType.Integer => parameter.AsInteger().ToString(),
                StorageType.Double => parameter.AsDouble().ToString("0.######"),
                StorageType.ElementId => DescribeElementIdValue(document, parameter.AsElementId()),
                _ => null
            };
		}
		catch
		{
			return null;
		}
	}

	private static string DescribeElementIdValue(Document document, ElementId id)
	{
		Element element = document.GetElement(id);
		return (element == null) ? id.Value.ToString() : $"{element.Name} ({id.Value})";
	}

	private static object SetParameter(Document document, JsonElement payload)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected O, but got Unknown
		//IL_01fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0204: Expected O, but got Unknown
		//IL_0206: Unknown result type (might be due to invalid IL or missing references)
		//IL_0222: Unknown result type (might be due to invalid IL or missing references)
		bool optionalBoolean = payload.GetOptionalBoolean("dryRun", defaultValue: true);
		long requiredInt = payload.GetRequiredInt64("elementId");
		string requiredString = payload.GetRequiredString("parameterName");
		JsonElement requiredProperty = payload.GetRequiredProperty("value");
		Element val = document.GetElement(new ElementId(requiredInt)) ?? throw new InvalidOperationException($"Element {requiredInt} was not found.");
		Parameter val2 = val.LookupParameter(requiredString) ?? throw new InvalidOperationException($"Element {requiredInt} does not have parameter '{requiredString}'.");
		if (((APIObject)val2).IsReadOnly)
		{
			throw new InvalidOperationException("Parameter '" + requiredString + "' is read-only.");
		}
		object obj = ParameterValueReader.Read(val2);
		object obj2 = ParameterValueReader.JsonValue(requiredProperty);
		if (!optionalBoolean)
		{
			string title = "Set parameter '" + requiredString + "'";
			string newLine = Environment.NewLine;
			InlineArray4<string> buffer = default(InlineArray4<string>);
			buffer[0] = $"Element: {val.Name} ({requiredInt})";
			buffer[1] = "Parameter: " + requiredString;
			buffer[2] = "Before: " + FormatApprovalValue(obj);
			buffer[3] = "Proposed: " + FormatApprovalValue(obj2);
			RequireWriteApproval(payload, title, string.Join(newLine, (ReadOnlySpan<string?>)buffer));
			Transaction val3 = new Transaction(document, "Codex set " + requiredString);
			try
			{
				val3.Start();
				ParameterValueWriter.Write(val2, requiredProperty, payload.GetOptionalString("doubleUnit"));
				val3.Commit();
			}
			finally
			{
				((IDisposable)val3)?.Dispose();
			}
		}
		object after = (optionalBoolean ? obj : ParameterValueReader.Read(val2));
		return new
		{
			dryRun = optionalBoolean,
			elementId = requiredInt,
			elementName = val.Name,
			parameterName = requiredString,
			before = obj,
			proposed = obj2,
			after = after
		};
	}

	private static object CreateWall(Document document, JsonElement payload)
	{
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Expected O, but got Unknown
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		bool optionalBoolean = payload.GetOptionalBoolean("dryRun", defaultValue: true);
		Level val = ResolveLevel(document, payload);
		WallType val2 = ResolveWallType(document, payload);
		JsonElement requiredProperty = payload.GetRequiredProperty("start");
		JsonElement requiredProperty2 = payload.GetRequiredProperty("end");
		double optionalDouble = payload.GetOptionalDouble("heightMm", 3000.0);
		XYZ val3 = ReadPointMm(requiredProperty);
		XYZ val4 = ReadPointMm(requiredProperty2);
		double num = MillimetersToFeet(optionalDouble);
		Line val5 = Line.CreateBound(val3, val4);
		Wall val6 = null;
		if (!optionalBoolean)
		{
			string newLine = Environment.NewLine;
			InlineArray5<string> buffer = default(InlineArray5<string>);
			buffer[0] = "Level: " + ((Element)val).Name;
			buffer[1] = "Wall type: " + ((Element)val2).Name;
			buffer[2] = "Start: " + FormatPointMm(val3);
			buffer[3] = "End: " + FormatPointMm(val4);
			buffer[4] = "Height: " + FormatNumber(optionalDouble) + " mm";
			RequireWriteApproval(payload, "Create wall", string.Join(newLine, (ReadOnlySpan<string?>)buffer));
			Transaction val7 = new Transaction(document, "Codex create wall");
			try
			{
				val7.Start();
				val6 = Wall.Create(document, (Curve)(object)val5, ((Element)val2).Id, ((Element)val).Id, num, 0.0, false, false);
				val7.Commit();
			}
			finally
			{
				((IDisposable)val7)?.Dispose();
			}
		}
		return new
		{
			dryRun = optionalBoolean,
			level = new
			{
				id = ((Element)val).Id.Value,
				Name = ((Element)val).Name
			},
			wallType = new
			{
				id = ((Element)val2).Id.Value,
				Name = ((Element)val2).Name
			},
			startMm = PointToMillimeters(val3),
			endMm = PointToMillimeters(val4),
			heightMm = optionalDouble,
			createdElementId = ((val6 != null) ? new long?(((Element)val6).Id.Value) : ((long?)null))
		};
	}

	private unsafe static object PlaceHostedFamilyInstance(Document document, JsonElement payload, BuiltInCategory category, string label)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Expected O, but got Unknown
		//IL_0159: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Expected O, but got Unknown
		//IL_0162: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		bool optionalBoolean = payload.GetOptionalBoolean("dryRun", defaultValue: true);
		Level val = ResolveLevel(document, payload);
		FamilySymbol val2 = ResolveFamilySymbol(document, payload, category);
		long requiredInt = payload.GetRequiredInt64("hostElementId");
		XYZ val3 = ReadPointMm(payload.GetRequiredProperty("location"));
		Element val4 = document.GetElement(new ElementId(requiredInt)) ?? throw new InvalidOperationException($"Host element {requiredInt} was not found.");
		FamilyInstance val5 = null;
		if (!optionalBoolean)
		{
			string title = "Place " + label;
			string newLine = Environment.NewLine;
			InlineArray4<string> buffer = default(InlineArray4<string>);
			buffer[0] = "Level: " + ((Element)val).Name;
			buffer[1] = "Symbol: " + ((ElementType)val2).FamilyName + " / " + ((Element)val2).Name;
			buffer[2] = $"Host ElementId: {requiredInt}";
			buffer[3] = "Location: " + FormatPointMm(val3);
			RequireWriteApproval(payload, title, string.Join(newLine, (ReadOnlySpan<string?>)buffer));
			Transaction val6 = new Transaction(document, "Codex place " + label);
			try
			{
				val6.Start();
				if (!val2.IsActive)
				{
					val2.Activate();
					document.Regenerate();
				}
				val5 = document.Create.NewFamilyInstance(val3, val2, val4, val, (StructuralType)0);
				val6.Commit();
			}
			finally
			{
				((IDisposable)val6)?.Dispose();
			}
		}
		return new
		{
			dryRun = optionalBoolean,
			category = ((object)(*(BuiltInCategory*)(&category))/*cast due to constrained. prefix*/).ToString(),
			level = new
			{
				id = ((Element)val).Id.Value,
				Name = ((Element)val).Name
			},
			symbol = new
			{
				id = ((Element)val2).Id.Value,
				FamilyName = ((ElementType)val2).FamilyName,
				typeName = ((Element)val2).Name
			},
			hostElementId = requiredInt,
			locationMm = PointToMillimeters(val3),
			createdElementId = ((val5 != null) ? new long?(((Element)val5).Id.Value) : ((long?)null))
		};
	}

	private static object CreateRoom(Document document, JsonElement payload)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Expected O, but got Unknown
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Expected O, but got Unknown
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		bool optionalBoolean = payload.GetOptionalBoolean("dryRun", defaultValue: true);
		Level val = ResolveLevel(document, payload);
		JsonElement requiredProperty = payload.GetRequiredProperty("location");
		XYZ val2 = ReadPointMm(requiredProperty);
		UV val3 = new UV(val2.X, val2.Y);
		Room val4 = null;
		if (!optionalBoolean)
		{
			string newLine = Environment.NewLine;
			InlineArray2<string> buffer = default(InlineArray2<string>);
			buffer[0] = "Level: " + ((Element)val).Name;
			buffer[1] = "Location: " + FormatPointMm(val2);
			RequireWriteApproval(payload, "Create room", string.Join(newLine, (ReadOnlySpan<string?>)buffer));
			Transaction val5 = new Transaction(document, "Codex create room");
			try
			{
				val5.Start();
				val4 = document.Create.NewRoom(val, val3);
				val5.Commit();
			}
			finally
			{
				((IDisposable)val5)?.Dispose();
			}
		}
		return new
		{
			dryRun = optionalBoolean,
			level = new
			{
				id = ((Element)val).Id.Value,
				Name = ((Element)val).Name
			},
			locationMm = PointToMillimeters(val2),
			createdElementId = ((val4 != null) ? new long?(((Element)val4).Id.Value) : ((long?)null)),
			roomName = ((val4 != null) ? ((Element)val4).Name : null),
			roomNumber = ((val4 != null) ? ((SpatialElement)val4).Number : null)
		};
	}

	private static object ExecuteBulkMutation(Document document, JsonElement payload, string title, string summary, Func<Document, JsonElement, object> execute)
	{
		if (!payload.GetOptionalBoolean("dryRun", defaultValue: true))
		{
			RequireWriteApproval(payload, title, summary);
		}
		return execute(document, payload);
	}

	private static void RequireWriteApproval(JsonElement payload, string title, string summary)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Invalid comparison between Unknown and I4
		if (payload.GetOptionalBoolean("confirmInRevit", defaultValue: true))
		{
			TaskDialog val = new TaskDialog("Revit Codex Bridge")
			{
				MainInstruction = title,
				MainContent = summary + Environment.NewLine + Environment.NewLine + "Confirm to write these changes to the active Revit model.",
				CommonButtons = (TaskDialogCommonButtons)9,
				DefaultButton = (TaskDialogResult)2
			};
			TaskDialogResult val2 = val.Show();
			if ((int)val2 != 1)
			{
				throw new InvalidOperationException("The Revit write operation was canceled by the user.");
			}
		}
	}

	private static IReadOnlyList<BatchOperation> ReadBatchOperations(JsonElement payload, bool batchDryRun)
	{
		if (!payload.TryGetProperty("operations", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidOperationException("run_batch requires an operations array.");
		}
		List<BatchOperation> list = new List<BatchOperation>();
		int num = 1;
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				throw new InvalidOperationException($"run_batch operation {num} must be an object.");
			}
			string text = item.GetRequiredString("command").ToLowerInvariant();
			if (text == "run_batch")
			{
				throw new InvalidOperationException("run_batch cannot contain another run_batch operation.");
			}
			string id = item.GetOptionalString("id") ?? $"op-{num:000}";
			string optionalString = item.GetOptionalString("description");
			JsonElement value2;
			JsonElement sourcePayload = ((item.TryGetProperty("payload", out value2) && value2.ValueKind == JsonValueKind.Object) ? value2 : item);
			Dictionary<string, object> dictionary = BuildBridgePayload(text, sourcePayload);
			bool flag = IsMutationCommand(text);
			bool flag2 = flag && (batchDryRun || item.GetOptionalBoolean("dryRun", defaultValue: true));
			dictionary["command"] = text;
			if (flag)
			{
				dictionary["dryRun"] = flag2;
				dictionary["confirmInRevit"] = false;
			}
			list.Add(new BatchOperation(id, text, optionalString, JsonSerializer.SerializeToElement(dictionary, JsonOptions).Clone(), flag, flag2));
			num++;
		}
		return list;
	}

	private static Dictionary<string, object?> BuildBridgePayload(string command, JsonElement sourcePayload)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		foreach (JsonProperty item in sourcePayload.EnumerateObject())
		{
			if (item.Value.ValueKind != JsonValueKind.Null && !item.NameEquals("id") && !item.NameEquals("description") && !item.NameEquals("requires") && !item.NameEquals("confidence") && !item.NameEquals("payload"))
			{
				dictionary[item.Name] = item.Value.Clone();
			}
		}
		if (!IsBridgeCommand(command))
		{
			throw new InvalidOperationException("run_batch does not support command '" + command + "'.");
		}
		return dictionary;
	}

	private static string BuildBatchApprovalSummary(IReadOnlyList<BatchOperation> operations, int writableMutations, bool atomic)
	{
		List<string> list = (from operation in operations.Take(8)
			select operation.Id + ": " + operation.Command + (operation.DryRun ? " (dry-run)" : " (write)") + (string.IsNullOrWhiteSpace(operation.Description) ? string.Empty : (" - " + operation.Description))).ToList();
		if (operations.Count > list.Count)
		{
			list.Add($"... {operations.Count - list.Count} more operations");
		}
		string newLine = Environment.NewLine;
		InlineArray5<string> buffer = default(InlineArray5<string>);
		buffer[0] = $"Operations: {operations.Count}";
		buffer[1] = $"Writable mutations: {writableMutations}";
		buffer[2] = "Transaction mode: " + (atomic ? "atomic, rollback all writes if any operation fails" : "non-atomic");
		buffer[3] = string.Empty;
		buffer[4] = string.Join(Environment.NewLine, list);
		return string.Join(newLine, (ReadOnlySpan<string?>)buffer);
	}

	private static bool IsBridgeCommand(string command)
	{
		switch (command)
		{
		case "prepare_revit_script":
		case "query_revit_script":
		case "execute_revit_script":
		case "list_grids":
		case "resolve_grid_region":
		case "list_structure_types":
		case "preview_steel_platform":
		case "create_steel_platform":
		case "get_model_context":
		case "find_elements":
		case "list_warnings":
		case "create_room_layout":
		case "get_active_document":
		case "list_levels":
		case "list_wall_types":
		case "list_family_symbols":
		case "count_elements":
		case "analyze_walls":
		case "get_selection":
		case "get_element":
		case "list_views":
		case "list_sheets":
		case "list_schedules":
		case "show_elements":
		case "finish_toolkit_info":
		case "finish_toolkit_run":
		case "create_wall":
		case "set_parameter":
		case "place_door":
		case "place_window":
		case "create_room":
		case "create_compound_wall_type":
		case "create_drawing_set":
		case "create_energy_cube_model":
			return true;
		default:
			return false;
		}
	}

	private static bool IsMutationCommand(string command)
	{
		switch (command)
		{
		case "execute_revit_script":
		case "create_steel_platform":
		case "create_room_layout":
		case "finish_toolkit_run":
		case "set_parameter":
		case "create_wall":
		case "place_door":
		case "place_window":
		case "create_room":
		case "create_compound_wall_type":
		case "create_drawing_set":
		case "create_energy_cube_model":
			return true;
		default:
			return false;
		}
	}

	private static string FormatApprovalValue(object? value)
	{
		if (1 == 0)
		{
		}
		string result = ((value == null) ? "null" : ((!(value is string text)) ? JsonSerializer.Serialize(value) : text));
		if (1 == 0)
		{
		}
		return result;
	}

	private static string FormatPointMm(XYZ point)
	{
		return $"({FormatNumber(FeetToMillimeters(point.X))}, {FormatNumber(FeetToMillimeters(point.Y))}, {FormatNumber(FeetToMillimeters(point.Z))}) mm";
	}

	private static string FormatNumber(double value)
	{
		return value.ToString("0.##", CultureInfo.InvariantCulture);
	}

	private static Level ResolveLevel(Document document, JsonElement payload)
	{
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected O, but got Unknown
		if (payload.TryGetProperty("levelId", out var value))
		{
			long @int = value.GetInt64();
			Element element = document.GetElement(new ElementId(@int));
			return (Level)(object)(((element is Level) ? element : null) ?? throw new InvalidOperationException($"Level {@int} was not found."));
		}
		string levelName = payload.GetOptionalString("levelName");
		IEnumerable<Level> source = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(Level))).Cast<Level>();
		if (!string.IsNullOrWhiteSpace(levelName))
		{
			return source.FirstOrDefault((Level level) => string.Equals(((Element)level).Name, levelName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Level '" + levelName + "' was not found.");
		}
		return source.OrderBy((Level level) => level.Elevation).FirstOrDefault() ?? throw new InvalidOperationException("The active document has no levels.");
	}

	private static WallType ResolveWallType(Document document, JsonElement payload)
	{
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected O, but got Unknown
		if (payload.TryGetProperty("wallTypeId", out var value))
		{
			long @int = value.GetInt64();
			Element element = document.GetElement(new ElementId(@int));
			return (WallType)(object)(((element is WallType) ? element : null) ?? throw new InvalidOperationException($"Wall type {@int} was not found."));
		}
		string wallTypeName = payload.GetOptionalString("wallTypeName");
		IEnumerable<WallType> source = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(WallType))).Cast<WallType>();
		if (!string.IsNullOrWhiteSpace(wallTypeName))
		{
			return source.FirstOrDefault((WallType type) => string.Equals(((Element)type).Name, wallTypeName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Wall type '" + wallTypeName + "' was not found.");
		}
		return source.FirstOrDefault() ?? throw new InvalidOperationException("The active document has no wall types.");
	}

	private static FamilySymbol ResolveFamilySymbol(Document document, JsonElement payload, BuiltInCategory category)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected O, but got Unknown
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected O, but got Unknown
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_018f: Unknown result type (might be due to invalid IL or missing references)
		if (payload.TryGetProperty("familySymbolId", out var value))
		{
			long @int = value.GetInt64();
			Element element = document.GetElement(new ElementId(@int));
			return (FamilySymbol)(object)(((element is FamilySymbol) ? element : null) ?? throw new InvalidOperationException($"Family symbol {@int} was not found."));
		}
		if (payload.TryGetProperty("typeId", out value))
		{
			long int2 = value.GetInt64();
			Element element2 = document.GetElement(new ElementId(int2));
			return (FamilySymbol)(object)(((element2 is FamilySymbol) ? element2 : null) ?? throw new InvalidOperationException($"Family symbol {int2} was not found."));
		}
		string familyName = payload.GetOptionalString("familyName");
		string typeName = payload.GetOptionalString("typeName");
		IEnumerable<FamilySymbol> source = ((IEnumerable)new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).OfCategory(category)).Cast<FamilySymbol>();
		if (!string.IsNullOrWhiteSpace(familyName) || !string.IsNullOrWhiteSpace(typeName))
		{
			FamilySymbol val = source.FirstOrDefault((FamilySymbol symbol) => (string.IsNullOrWhiteSpace(familyName) || string.Equals(((ElementType)symbol).FamilyName, familyName, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(typeName) || string.Equals(((Element)symbol).Name, typeName, StringComparison.OrdinalIgnoreCase)));
			return val ?? throw new InvalidOperationException($"Family symbol was not found for category {category}, family '{familyName}', type '{typeName}'.");
		}
		return source.FirstOrDefault() ?? throw new InvalidOperationException($"The active document has no symbols for category {category}.");
	}

	private static XYZ ReadPointMm(JsonElement point)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Expected O, but got Unknown
		return new XYZ(MillimetersToFeet(point.GetRequiredDouble("xMm")), MillimetersToFeet(point.GetRequiredDouble("yMm")), MillimetersToFeet(point.GetOptionalDouble("zMm", 0.0)));
	}

	private static object PointToMillimeters(XYZ point)
	{
		return new
		{
			xMm = FeetToMillimeters(point.X),
			yMm = FeetToMillimeters(point.Y),
			zMm = FeetToMillimeters(point.Z)
		};
	}

	private static double MillimetersToFeet(double value)
	{
		return value / 304.8;
	}

	private static double FeetToMillimeters(double value)
	{
		return value * 304.8;
	}
}
