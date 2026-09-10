using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RevitCodexBridge.Addin;

internal sealed class BridgeServer : IDisposable
{
    public const int DefaultPort = 7878;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly BridgeRuntime _runtime;
    private readonly int _port;
    private readonly CancellationTokenSource _shutdown = new();
    private TcpListener? _listener;
    private Task? _serverTask;

    public BridgeServer(BridgeRuntime runtime, int port)
    {
        _runtime = runtime;
        _port = port;
    }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _serverTask = Task.Run(() => RunAsync(_shutdown.Token));
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _listener?.Stop();

        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown should never block Revit closing.
        }

        _shutdown.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var ownedClient = client;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            var stream = ownedClient.GetStream();
            var request = await LocalHttpRequest.ReadAsync(stream, timeout.Token).ConfigureAwait(false);
            var response = await HandleRequestAsync(request, timeout.Token).ConfigureAwait(false);
            await WriteJsonAsync(stream, response.StatusCode, response.Payload, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(
                    client.GetStream(),
                    500,
                    new BridgeEnvelope(false, null, ex.Message),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The client may already be gone.
            }
        }
    }

    private async Task<BridgeHttpResponse> HandleRequestAsync(LocalHttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Method == "GET" && request.Path == "/health")
        {
            return new BridgeHttpResponse(
                200,
                new BridgeEnvelope(
                    true,
                    new
                    {
                        bridge = "revit-codex-bridge",
                        revitVersion = _runtime.RevitVersion,
                        url = _runtime.ServerUrl,
                        pending = _runtime.PendingCount,
                        startedAtUtc = _runtime.StartedAtUtc,
                        processed = _runtime.ProcessedCount,
                        failed = _runtime.FailedCount,
                        lastCommand = _runtime.LastCommand,
                        lastError = _runtime.LastError,
                        logPath = BridgeLog.LogPath,
                        commands = new[]
                        {
                            "run_batch",
                            "get_active_document",
                            "get_model_context",
                            "list_grids",
                            "resolve_grid_region",
                            "list_structure_types",
                            "preview_steel_platform",
                            "create_steel_platform",
                            "find_elements",
                            "list_warnings",
                            "create_room_layout",
                            "create_compound_wall_type",
                            "list_levels",
                            "list_wall_types",
                            "list_family_symbols",
                            "count_elements",
                            "analyze_walls",
                            "get_selection",
                            "get_element",
                            "list_views",
                            "list_sheets",
                            "list_schedules",
                            "show_elements",
                            "finish_toolkit_info",
                            "finish_toolkit_run",
                            "set_parameter",
                            "create_wall",
                            "place_door",
                            "place_window",
                            "create_room",
                            "create_drawing_set",
                            "create_energy_cube_model"
                        }
                    },
                    null));
        }

        if (request.Method == "POST" && request.Path == "/commands")
        {
            using var document = JsonDocument.Parse(request.Body);
            var result = await _runtime
                .EnqueueAsync(document.RootElement, TimeSpan.FromMinutes(2))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return new BridgeHttpResponse(200, new BridgeEnvelope(true, result, null));
        }

        return new BridgeHttpResponse(404, new BridgeEnvelope(false, null, "Unknown endpoint."));
    }

    private static async Task WriteJsonAsync(
        NetworkStream stream,
        int statusCode,
        object payload,
        CancellationToken cancellationToken)
    {
        var statusText = statusCode == 200 ? "OK" : "ERROR";
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {statusText}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private sealed record BridgeEnvelope(bool Ok, object? Result, string? Error);

    private sealed record BridgeHttpResponse(int StatusCode, object Payload);
}
