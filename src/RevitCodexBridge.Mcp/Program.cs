using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitCodexBridge.Mcp;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static async Task<int> Main()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);

        var bridgeUrl = Environment.GetEnvironmentVariable("REVIT_CODEX_BRIDGE_URL") ?? "http://127.0.0.1:7878";
        using var httpClient = new HttpClient { BaseAddress = new Uri(bridgeUrl) };
        await using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        var server = new McpServer(httpClient, output);

        string? line;
        while ((line = await input.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await server.HandleLineAsync(line).ConfigureAwait(false);
        }

        return 0;
    }

    private sealed class McpServer
    {
        private const string ProtocolVersion = "2024-11-05";

        private readonly HttpClient _httpClient;
        private readonly StreamWriter _output;

        public McpServer(HttpClient httpClient, StreamWriter output)
        {
            _httpClient = httpClient;
            _output = output;
        }

        public async Task HandleLineAsync(string line)
        {
            JsonElement? id = null;

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                id = root.TryGetProperty("id", out var idProperty) ? idProperty.Clone() : null;

                if (!root.TryGetProperty("method", out var methodProperty))
                {
                    if (id is not null)
                    {
                        await WriteErrorAsync(id.Value, -32600, "Missing JSON-RPC method.").ConfigureAwait(false);
                    }

                    return;
                }

                var method = methodProperty.GetString();
                if (id is null)
                {
                    return;
                }

                var result = method switch
                {
                    "initialize" => InitializeResult(),
                    "ping" => new JsonObject(),
                    "tools/list" => ToolsListResult(),
                    "tools/call" => await CallToolAsync(root.GetProperty("params")).ConfigureAwait(false),
                    _ => null
                };

                if (result is null)
                {
                    await WriteErrorAsync(id.Value, -32601, $"Unsupported method '{method}'.").ConfigureAwait(false);
                    return;
                }

                await WriteResultAsync(id.Value, result).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (id is null)
                {
                    await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
                    return;
                }

                await WriteErrorAsync(id.Value, -32603, ex.Message).ConfigureAwait(false);
            }
        }

        private static JsonObject InitializeResult()
        {
            return new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject()
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "revit-codex-bridge",
                    ["version"] = "0.2.0"
                }
            };
        }

        private static JsonObject ToolsListResult()
        {
            var tools = new JsonArray
            {
                Tool("revit_health", "Check whether the local Revit bridge is running.", EmptySchema()),
                Tool("revit_get_active_document", "Return the current Revit document and active view.", EmptySchema()),
                Tool("revit_list_levels", "List Revit levels in the active document.", EmptySchema()),
                Tool("revit_list_wall_types", "List wall types in the active document.", EmptySchema()),
                Tool(
                    "revit_list_family_symbols",
                    "List family symbols for a Revit BuiltInCategory such as OST_Doors or OST_Windows.",
                    ObjectSchema(("category", "string", "Revit BuiltInCategory name, for example OST_Doors.", true))),
                Tool(
                    "revit_count_elements",
                    "Count model elements, optionally filtered by Revit BuiltInCategory.",
                    ObjectSchema(("category", "string", "Optional Revit BuiltInCategory name.", false))),
                Tool(
                    "revit_run_batch",
                    "Run multiple bridge commands or BuildPlan operations in one request. Mutations are dry-run by default and can be atomic.",
                    BatchSchema()),
                Tool(
                    "revit_create_wall",
                    "Create or dry-run a straight wall using millimeter coordinates.",
                    ObjectSchema(
                        ("levelName", "string", "Target level name.", true),
                        ("startXmm", "number", "Start X coordinate in millimeters.", true),
                        ("startYmm", "number", "Start Y coordinate in millimeters.", true),
                        ("endXmm", "number", "End X coordinate in millimeters.", true),
                        ("endYmm", "number", "End Y coordinate in millimeters.", true),
                        ("heightMm", "number", "Wall height in millimeters.", true),
                        ("wallTypeName", "string", "Optional wall type name.", false),
                        ("dryRun", "boolean", "Defaults to true. Set false only after user approval.", false),
                        ("confirmInRevit", "boolean", "Defaults to true for writes. Shows a Revit confirmation dialog.", false))),
                Tool(
                    "revit_set_parameter",
                    "Set or dry-run one editable parameter on a Revit element.",
                    ObjectSchema(
                        ("elementId", "integer", "Target Revit ElementId.", true),
                        ("parameterName", "string", "Parameter display name.", true),
                        ("value", "string", "New value. Numbers and booleans are also accepted.", true),
                        ("doubleUnit", "string", "Optional unit for numeric doubles, for example mm.", false),
                        ("dryRun", "boolean", "Defaults to true. Set false only after user approval.", false),
                        ("confirmInRevit", "boolean", "Defaults to true for writes. Shows a Revit confirmation dialog.", false))),
                Tool(
                    "revit_place_door",
                    "Place or dry-run a door hosted by an existing wall.",
                    HostedFamilySchema()),
                Tool(
                    "revit_place_window",
                    "Place or dry-run a window hosted by an existing wall.",
                    HostedFamilySchema()),
                Tool(
                    "revit_create_room",
                    "Create or dry-run a room at a millimeter coordinate on a level.",
                    ObjectSchema(
                        ("levelName", "string", "Target level name.", true),
                        ("xMm", "number", "Room point X coordinate in millimeters.", true),
                        ("yMm", "number", "Room point Y coordinate in millimeters.", true),
                        ("zMm", "number", "Room point Z coordinate in millimeters. Defaults to 0.", false),
                        ("dryRun", "boolean", "Defaults to true. Set false only after user approval.", false),
                        ("confirmInRevit", "boolean", "Defaults to true for writes. Shows a Revit confirmation dialog.", false)))
            };

            return new JsonObject
            {
                ["tools"] = tools
            };
        }

        private async Task<JsonObject> CallToolAsync(JsonElement parameters)
        {
            var name = parameters.GetProperty("name").GetString()
                ?? throw new InvalidOperationException("tools/call params.name is required.");
            var arguments = parameters.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object
                ? args
                : default;

            try
            {
                var text = name switch
                {
                    "revit_health" => await GetBridgeAsync("/health").ConfigureAwait(false),
                    "revit_get_active_document" => await PostCommandAsync(new Dictionary<string, object?>
                    {
                        ["command"] = "get_active_document"
                    }).ConfigureAwait(false),
                    "revit_list_levels" => await PostCommandAsync(new Dictionary<string, object?>
                    {
                        ["command"] = "list_levels"
                    }).ConfigureAwait(false),
                    "revit_list_wall_types" => await PostCommandAsync(new Dictionary<string, object?>
                    {
                        ["command"] = "list_wall_types"
                    }).ConfigureAwait(false),
                    "revit_list_family_symbols" => await PostCommandAsync(new Dictionary<string, object?>
                    {
                        ["command"] = "list_family_symbols",
                        ["category"] = RequiredString(arguments, "category")
                    }).ConfigureAwait(false),
                    "revit_count_elements" => await PostCommandAsync(CountElementsCommand(arguments)).ConfigureAwait(false),
                    "revit_run_batch" => await PostCommandAsync(RunBatchCommand(arguments)).ConfigureAwait(false),
                    "revit_create_wall" => await PostCommandAsync(CreateWallCommand(arguments)).ConfigureAwait(false),
                    "revit_set_parameter" => await PostCommandAsync(SetParameterCommand(arguments)).ConfigureAwait(false),
                    "revit_place_door" => await PostCommandAsync(HostedFamilyCommand(arguments, "place_door")).ConfigureAwait(false),
                    "revit_place_window" => await PostCommandAsync(HostedFamilyCommand(arguments, "place_window")).ConfigureAwait(false),
                    "revit_create_room" => await PostCommandAsync(CreateRoomCommand(arguments)).ConfigureAwait(false),
                    _ => throw new InvalidOperationException($"Unknown tool '{name}'.")
                };

                return ToolContent(text, isError: false);
            }
            catch (Exception ex)
            {
                return ToolContent(ex.Message, isError: true);
            }
        }

        private async Task<string> GetBridgeAsync(string path)
        {
            using var response = await _httpClient.GetAsync(path).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? FormatJson(text)
                : throw new InvalidOperationException($"Bridge returned HTTP {(int)response.StatusCode}: {text}");
        }

        private async Task<string> PostCommandAsync(Dictionary<string, object?> payload)
        {
            using var response = await _httpClient.PostAsync("/commands", ToJsonContent(payload)).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? FormatJson(text)
                : throw new InvalidOperationException($"Bridge returned HTTP {(int)response.StatusCode}: {text}");
        }

        private static StringContent ToJsonContent(object payload)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            return new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);
        }

        private static Dictionary<string, object?> CountElementsCommand(JsonElement args)
        {
            var payload = new Dictionary<string, object?>
            {
                ["command"] = "count_elements"
            };

            CopyString(args, payload, "category");
            return payload;
        }

        private static Dictionary<string, object?> RunBatchCommand(JsonElement args)
        {
            var operations = RequiredElement(args, "operations");
            if (operations.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Argument 'operations' must be an array.");
            }

            return new Dictionary<string, object?>
            {
                ["command"] = "run_batch",
                ["operations"] = operations.Clone(),
                ["dryRun"] = OptionalBool(args, "dryRun", true),
                ["atomic"] = OptionalBool(args, "atomic", true),
                ["continueOnError"] = OptionalBool(args, "continueOnError", false),
                ["confirmInRevit"] = OptionalBool(args, "confirmInRevit", true)
            };
        }

        private static Dictionary<string, object?> CreateWallCommand(JsonElement args)
        {
            var payload = new Dictionary<string, object?>
            {
                ["command"] = "create_wall",
                ["levelName"] = RequiredString(args, "levelName"),
                ["start"] = Point(RequiredDouble(args, "startXmm"), RequiredDouble(args, "startYmm"), OptionalDouble(args, "startZmm", 0)),
                ["end"] = Point(RequiredDouble(args, "endXmm"), RequiredDouble(args, "endYmm"), OptionalDouble(args, "endZmm", 0)),
                ["heightMm"] = RequiredDouble(args, "heightMm"),
                ["dryRun"] = OptionalBool(args, "dryRun", true),
                ["confirmInRevit"] = OptionalBool(args, "confirmInRevit", true)
            };

            CopyString(args, payload, "wallTypeName");
            return payload;
        }

        private static Dictionary<string, object?> SetParameterCommand(JsonElement args)
        {
            var payload = new Dictionary<string, object?>
            {
                ["command"] = "set_parameter",
                ["elementId"] = RequiredInt64(args, "elementId"),
                ["parameterName"] = RequiredString(args, "parameterName"),
                ["value"] = RequiredElement(args, "value").Clone(),
                ["dryRun"] = OptionalBool(args, "dryRun", true),
                ["confirmInRevit"] = OptionalBool(args, "confirmInRevit", true)
            };

            CopyString(args, payload, "doubleUnit");
            return payload;
        }

        private static Dictionary<string, object?> HostedFamilyCommand(JsonElement args, string command)
        {
            var payload = new Dictionary<string, object?>
            {
                ["command"] = command,
                ["hostElementId"] = RequiredInt64(args, "hostElementId"),
                ["levelName"] = RequiredString(args, "levelName"),
                ["location"] = Point(
                    RequiredDouble(args, "xMm"),
                    RequiredDouble(args, "yMm"),
                    OptionalDouble(args, "zMm", 0)),
                ["dryRun"] = OptionalBool(args, "dryRun", true),
                ["confirmInRevit"] = OptionalBool(args, "confirmInRevit", true)
            };

            CopyString(args, payload, "familyName");
            CopyString(args, payload, "typeName");
            return payload;
        }

        private static Dictionary<string, object?> CreateRoomCommand(JsonElement args)
        {
            return new Dictionary<string, object?>
            {
                ["command"] = "create_room",
                ["levelName"] = RequiredString(args, "levelName"),
                ["location"] = Point(
                    RequiredDouble(args, "xMm"),
                    RequiredDouble(args, "yMm"),
                    OptionalDouble(args, "zMm", 0)),
                ["dryRun"] = OptionalBool(args, "dryRun", true),
                ["confirmInRevit"] = OptionalBool(args, "confirmInRevit", true)
            };
        }

        private static Dictionary<string, object?> Point(double xMm, double yMm, double zMm)
        {
            return new Dictionary<string, object?>
            {
                ["xMm"] = xMm,
                ["yMm"] = yMm,
                ["zMm"] = zMm
            };
        }

        private static void CopyString(JsonElement args, Dictionary<string, object?> payload, string name)
        {
            if (args.ValueKind == JsonValueKind.Object &&
                args.TryGetProperty(name, out var value) &&
                value.ValueKind != JsonValueKind.Null)
            {
                payload[name] = value.GetString();
            }
        }

        private static JsonElement RequiredElement(JsonElement args, string name)
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
            {
                throw new InvalidOperationException($"Missing required argument '{name}'.");
            }

            return value;
        }

        private static string RequiredString(JsonElement args, string name)
        {
            var value = RequiredElement(args, name).GetString();
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"Argument '{name}' must be a non-empty string.")
                : value;
        }

        private static long RequiredInt64(JsonElement args, string name)
        {
            return RequiredElement(args, name).GetInt64();
        }

        private static double RequiredDouble(JsonElement args, string name)
        {
            return RequiredElement(args, name).GetDouble();
        }

        private static double OptionalDouble(JsonElement args, string name, double defaultValue)
        {
            return args.ValueKind == JsonValueKind.Object &&
                   args.TryGetProperty(name, out var value) &&
                   value.ValueKind != JsonValueKind.Null
                ? value.GetDouble()
                : defaultValue;
        }

        private static bool OptionalBool(JsonElement args, string name, bool defaultValue)
        {
            return args.ValueKind == JsonValueKind.Object &&
                   args.TryGetProperty(name, out var value) &&
                   value.ValueKind != JsonValueKind.Null
                ? value.GetBoolean()
                : defaultValue;
        }

        private static JsonObject ToolContent(string text, bool isError)
        {
            return new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = text
                    }
                },
                ["isError"] = isError
            };
        }

        private static JsonObject Tool(string name, string description, JsonObject inputSchema)
        {
            return new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = inputSchema
            };
        }

        private static JsonObject EmptySchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject()
            };
        }

        private static JsonObject HostedFamilySchema()
        {
            return ObjectSchema(
                ("hostElementId", "integer", "Wall ElementId that hosts the family instance.", true),
                ("levelName", "string", "Target level name.", true),
                ("xMm", "number", "Insertion X coordinate in millimeters.", true),
                ("yMm", "number", "Insertion Y coordinate in millimeters.", true),
                ("zMm", "number", "Insertion Z coordinate in millimeters. Defaults to 0.", false),
                ("familyName", "string", "Optional family name.", false),
                ("typeName", "string", "Optional type name.", false),
                ("dryRun", "boolean", "Defaults to true. Set false only after user approval.", false),
                ("confirmInRevit", "boolean", "Defaults to true for writes. Shows a Revit confirmation dialog.", false));
        }

        private static JsonObject BatchSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray { "operations" },
                ["properties"] = new JsonObject
                {
                    ["operations"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["description"] = "Array of bridge commands or BuildPlan operations. Each item must include a command.",
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = true
                        }
                    },
                    ["dryRun"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Defaults to true. When true, all mutation operations are forced to dry-run."
                    },
                    ["atomic"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Defaults to true. Rolls back all writes when any operation fails."
                    },
                    ["continueOnError"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Defaults to false. Continue executing later operations after a failure."
                    },
                    ["confirmInRevit"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Defaults to true for writes. Shows one Revit confirmation dialog for the whole batch."
                    }
                }
            };
        }

        private static JsonObject ObjectSchema(params (string Name, string Type, string Description, bool Required)[] properties)
        {
            var schemaProperties = new JsonObject();
            var required = new JsonArray();

            foreach (var property in properties)
            {
                schemaProperties[property.Name] = new JsonObject
                {
                    ["type"] = property.Type,
                    ["description"] = property.Description
                };

                if (property.Required)
                {
                    required.Add(property.Name);
                }
            }

            var schema = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = schemaProperties
            };

            if (required.Count > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        private async Task WriteResultAsync(JsonElement id, JsonNode result)
        {
            var response = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonNode.Parse(id.GetRawText()),
                ["result"] = result
            };

            await WriteAsync(response).ConfigureAwait(false);
        }

        private async Task WriteErrorAsync(JsonElement id, int code, string message)
        {
            var response = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonNode.Parse(id.GetRawText()),
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            };

            await WriteAsync(response).ConfigureAwait(false);
        }

        private async Task WriteAsync(JsonNode response)
        {
            await _output.WriteLineAsync(response.ToJsonString(JsonOptions)).ConfigureAwait(false);
        }

        private static string FormatJson(string text)
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                });
        }
    }
}
