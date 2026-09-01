using System.Globalization;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

namespace RevitCodexBridge.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var url = Environment.GetEnvironmentVariable("REVIT_CODEX_BRIDGE_URL") ?? "http://127.0.0.1:7878";

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            var command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "health":
                    await PrintHealthAsync(client);
                    return 0;

                case "doc":
                    await PostCommandAsync(client, new Dictionary<string, object?> { ["command"] = "get_active_document" });
                    return 0;

                case "levels":
                    await PostCommandAsync(client, new Dictionary<string, object?> { ["command"] = "list_levels" });
                    return 0;

                case "wall-types":
                    await PostCommandAsync(client, new Dictionary<string, object?> { ["command"] = "list_wall_types" });
                    return 0;

                case "family-symbols":
                    await PostCommandAsync(client, BuildListFamilySymbolsCommand(args));
                    return 0;

                case "door-types":
                    await PostCommandAsync(client, new Dictionary<string, object?>
                    {
                        ["command"] = "list_family_symbols",
                        ["category"] = "OST_Doors"
                    });
                    return 0;

                case "window-types":
                    await PostCommandAsync(client, new Dictionary<string, object?>
                    {
                        ["command"] = "list_family_symbols",
                        ["category"] = "OST_Windows"
                    });
                    return 0;

                case "count":
                    await PostCommandAsync(client, BuildCountCommand(args));
                    return 0;

                case "set-param":
                    await PostCommandAsync(client, BuildSetParameterCommand(args));
                    return 0;

                case "create-wall":
                    await PostCommandAsync(client, BuildCreateWallCommand(args));
                    return 0;

                case "run-json":
                    await PostCommandJsonAsync(client, ReadJsonCommand(args));
                    return 0;

                case "run-dir":
                    await RunCommandDirectoryAsync(client, args);
                    return 0;

                case "run-plan":
                    await RunBuildPlanAsync(client, args);
                    return 0;

                case "wait":
                    await WaitForBridgeAsync(client, args);
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                    PrintHelp();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task PrintHealthAsync(HttpClient client)
    {
        var text = await client.GetStringAsync("/health");
        Console.WriteLine(FormatJson(text));
    }

    private static async Task WaitForBridgeAsync(HttpClient client, string[] args)
    {
        var timeoutSeconds = args.Length > 1 && int.TryParse(args[1], out var parsed)
            ? parsed
            : 60;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await PrintHealthAsync(client);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException(
            $"Bridge did not become available within {timeoutSeconds} seconds. Last error: {lastError?.Message}");
    }

    private static async Task PostCommandAsync(HttpClient client, object payload)
    {
        using var response = await client.PostAsync("/commands", ToJsonContent(payload));
        var text = await response.Content.ReadAsStringAsync();
        Console.WriteLine(FormatJson(text));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Bridge returned HTTP {(int)response.StatusCode}.");
        }
    }

    private static async Task PostCommandJsonAsync(HttpClient client, JsonElement payload)
    {
        using var response = await client.PostAsync("/commands", ToJsonContent(payload));
        var text = await response.Content.ReadAsStringAsync();
        Console.WriteLine(FormatJson(text));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Bridge returned HTTP {(int)response.StatusCode}.");
        }
    }

    private static StringContent ToJsonContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);
    }

    private static JsonElement ReadJsonCommand(string[] args)
    {
        if (args.Length < 2)
        {
            throw new InvalidOperationException("Usage: revitctl run-json <command.json>");
        }

        var json = File.ReadAllText(args[1], Encoding.UTF8);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task RunCommandDirectoryAsync(HttpClient client, string[] args)
    {
        if (args.Length < 2)
        {
            throw new InvalidOperationException("Usage: revitctl run-dir <directory>");
        }

        var files = Directory
            .EnumerateFiles(args[1], "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No JSON command files found in '{args[1]}'.");
        }

        foreach (var file in files)
        {
            Console.WriteLine($"Running {file}");
            await PostCommandJsonAsync(client, ReadJsonCommand(new[] { "run-json", file }));
        }
    }

    private static async Task RunBuildPlanAsync(HttpClient client, string[] args)
    {
        if (args.Length < 2)
        {
            throw new InvalidOperationException("Usage: revitctl run-plan <buildplan.json> [--write] [--batch] [--non-atomic] [--continue-on-error]");
        }

        var allowWrites = args.Contains("--write");
        var useBatch = args.Contains("--batch");
        var json = File.ReadAllText(args[1], Encoding.UTF8);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("operations", out var operations) ||
            operations.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("BuildPlan must contain an operations array.");
        }

        if (useBatch)
        {
            await RunBuildPlanBatchAsync(client, operations, allowWrites, args);
            return;
        }

        foreach (var operation in operations.EnumerateArray())
        {
            var id = operation.TryGetProperty("id", out var idProperty)
                ? idProperty.GetString()
                : "<no-id>";
            var command = operation.TryGetProperty("command", out var commandProperty)
                ? commandProperty.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var bridgeCommand = ConvertPlanOperation(operation, allowWrites);
            if (bridgeCommand is null)
            {
                Console.WriteLine($"Skipping unsupported operation {id}: {command}");
                continue;
            }

            Console.WriteLine($"Running {id}: {command}");
            await PostCommandAsync(client, bridgeCommand);
        }
    }

    private static async Task RunBuildPlanBatchAsync(
        HttpClient client,
        JsonElement operations,
        bool allowWrites,
        string[] args)
    {
        var bridgeOperations = new List<Dictionary<string, object?>>();

        foreach (var operation in operations.EnumerateArray())
        {
            var id = operation.TryGetProperty("id", out var idProperty)
                ? idProperty.GetString()
                : null;
            var command = operation.TryGetProperty("command", out var commandProperty)
                ? commandProperty.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var bridgeCommand = ConvertPlanOperation(operation, allowWrites);
            if (bridgeCommand is null)
            {
                Console.WriteLine($"Skipping unsupported operation {id ?? "<no-id>"}: {command}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                bridgeCommand["id"] = id;
            }

            if (operation.TryGetProperty("description", out var descriptionProperty) &&
                descriptionProperty.ValueKind == JsonValueKind.String)
            {
                bridgeCommand["description"] = descriptionProperty.GetString();
            }

            bridgeOperations.Add(bridgeCommand);
        }

        if (bridgeOperations.Count == 0)
        {
            throw new InvalidOperationException("No executable operations were found in the BuildPlan.");
        }

        var batchPayload = new Dictionary<string, object?>
        {
            ["command"] = "run_batch",
            ["dryRun"] = !allowWrites,
            ["atomic"] = !args.Contains("--non-atomic"),
            ["continueOnError"] = args.Contains("--continue-on-error"),
            ["operations"] = bridgeOperations
        };

        Console.WriteLine($"Running batch: {bridgeOperations.Count} operations");
        await PostCommandAsync(client, batchPayload);
    }

    private static Dictionary<string, object?>? ConvertPlanOperation(JsonElement operation, bool allowWrites)
    {
        var command = operation.GetProperty("command").GetString();
        if (string.IsNullOrWhiteSpace(command) || !IsBridgeCommand(command))
        {
            return null;
        }

        var result = new Dictionary<string, object?>
        {
            ["command"] = command
        };

        if (operation.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Null)
                {
                    result[property.Name] = property.Value.Clone();
                }
            }
        }

        if (IsMutationCommand(command))
        {
            var operationAllowsWrite =
                operation.TryGetProperty("dryRun", out var dryRun) &&
                dryRun.ValueKind == JsonValueKind.False;

            result["dryRun"] = allowWrites && operationAllowsWrite ? false : true;
        }

        return result;
    }

    private static bool IsBridgeCommand(string command)
    {
        return command is
            "get_active_document" or
            "list_levels" or
            "list_wall_types" or
            "list_family_symbols" or
            "count_elements" or
            "create_wall" or
            "set_parameter" or
            "place_door" or
            "place_window" or
            "create_room" or
            "create_drawing_set" or
            "create_energy_cube_model";
    }

    private static bool IsMutationCommand(string command)
    {
        return command is "create_wall" or "set_parameter" or "place_door" or "place_window" or "create_room" or "create_drawing_set" or "create_energy_cube_model";
    }

    private static Dictionary<string, object?> BuildCountCommand(string[] args)
    {
        var payload = new Dictionary<string, object?>
        {
            ["command"] = "count_elements"
        };

        if (args.Length > 1)
        {
            payload["category"] = args[1];
        }

        return payload;
    }

    private static Dictionary<string, object?> BuildListFamilySymbolsCommand(string[] args)
    {
        if (args.Length < 2)
        {
            throw new InvalidOperationException("Usage: revitctl family-symbols <BuiltInCategory>");
        }

        return new Dictionary<string, object?>
        {
            ["command"] = "list_family_symbols",
            ["category"] = args[1]
        };
    }

    private static Dictionary<string, object?> BuildSetParameterCommand(string[] args)
    {
        if (args.Length < 4)
        {
            throw new InvalidOperationException(
                "Usage: revitctl set-param <elementId> <parameterName> <value> [--write] [--double-unit mm]");
        }

        return new Dictionary<string, object?>
        {
            ["command"] = "set_parameter",
            ["elementId"] = long.Parse(args[1]),
            ["parameterName"] = args[2],
            ["value"] = ParseScalar(args[3]),
            ["dryRun"] = !args.Contains("--write"),
            ["doubleUnit"] = ReadOption(args, "--double-unit")
        };
    }

    private static Dictionary<string, object?> BuildCreateWallCommand(string[] args)
    {
        if (args.Length < 7)
        {
            throw new InvalidOperationException(
                "Usage: revitctl create-wall <levelName> <startXmm> <startYmm> <endXmm> <endYmm> <heightMm> [--write]");
        }

        var payload = new Dictionary<string, object?>
        {
            ["command"] = "create_wall",
            ["levelName"] = args[1],
            ["start"] = new Dictionary<string, object?>
            {
                ["xMm"] = double.Parse(args[2], CultureInfo.InvariantCulture),
                ["yMm"] = double.Parse(args[3], CultureInfo.InvariantCulture)
            },
            ["end"] = new Dictionary<string, object?>
            {
                ["xMm"] = double.Parse(args[4], CultureInfo.InvariantCulture),
                ["yMm"] = double.Parse(args[5], CultureInfo.InvariantCulture)
            },
            ["heightMm"] = double.Parse(args[6], CultureInfo.InvariantCulture),
            ["dryRun"] = !args.Contains("--write")
        };

        var wallTypeName = ReadOption(args, "--wall-type");
        if (!string.IsNullOrWhiteSpace(wallTypeName))
        {
            payload["wallTypeName"] = wallTypeName;
        }

        return payload;
    }

    private static object ParseScalar(string raw)
    {
        if (long.TryParse(raw, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        if (bool.TryParse(raw, out var boolValue))
        {
            return boolValue;
        }

        return raw;
    }

    private static string? ReadOption(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string FormatJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        return JsonSerializer.Serialize(document.RootElement, JsonOptions);
    }

    private static void PrintHelp()
    {
        var help = new StringBuilder();
        help.AppendLine("revitctl - command line client for YingTan Revit AI Bridge");
        help.AppendLine();
        help.AppendLine("Commands:");
        help.AppendLine("  revitctl health");
        help.AppendLine("  revitctl doc");
        help.AppendLine("  revitctl levels");
        help.AppendLine("  revitctl wall-types");
        help.AppendLine("  revitctl door-types");
        help.AppendLine("  revitctl window-types");
        help.AppendLine("  revitctl family-symbols <BuiltInCategory>");
        help.AppendLine("  revitctl count [BuiltInCategory]");
        help.AppendLine("  revitctl set-param <elementId> <parameterName> <value> [--write] [--double-unit mm]");
        help.AppendLine("  revitctl create-wall <levelName> <startXmm> <startYmm> <endXmm> <endYmm> <heightMm> [--write] [--wall-type name]");
        help.AppendLine("  revitctl run-json <command.json>");
        help.AppendLine("  revitctl run-dir <directory>");
        help.AppendLine("  revitctl run-plan <buildplan.json> [--write] [--batch] [--non-atomic] [--continue-on-error]");
        help.AppendLine("  revitctl wait [timeoutSeconds]");
        help.AppendLine();
        help.AppendLine("Environment:");
        help.AppendLine("  REVIT_CODEX_BRIDGE_URL=http://127.0.0.1:7878");
        Console.WriteLine(help.ToString());
    }
}
