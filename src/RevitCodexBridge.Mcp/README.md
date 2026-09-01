# RevitCodexBridge.Mcp

This project implements the Codex MCP wrapper.

The MCP server is intentionally thin. It speaks MCP over stdio, validates tool
arguments, forwards safe JSON commands to the local Revit bridge, and returns the
bridge JSON response. Revit API calls still happen only inside the Revit add-in
process.

## Run

Build first:

```powershell
dotnet build .\RevitCodexBridge.sln
```

Then point an MCP client at:

```powershell
dotnet run --project .\src\RevitCodexBridge.Mcp\RevitCodexBridge.Mcp.csproj
```

If the Revit bridge uses a non-default URL:

```powershell
$env:REVIT_CODEX_BRIDGE_URL = "http://127.0.0.1:7878"
dotnet run --project .\src\RevitCodexBridge.Mcp\RevitCodexBridge.Mcp.csproj
```

## Tools

- `revit_health`
- `revit_get_active_document`
- `revit_list_levels`
- `revit_list_wall_types`
- `revit_list_family_symbols`
- `revit_count_elements`
- `revit_run_batch`
- `revit_create_wall`
- `revit_set_parameter`
- `revit_place_door`
- `revit_place_window`
- `revit_create_room`

Mutation tools default to `dryRun=true`. If a tool is called with `dryRun=false`,
the Revit add-in also shows a confirmation dialog by default before starting a
transaction. Advanced callers can set `confirmInRevit=false` only when they have
their own approval UI.

`revit_run_batch` accepts an `operations` array containing either bridge commands
or BuildPlan operations. It defaults to `dryRun=true` and `atomic=true`; when a
batch is written with `dryRun=false`, the add-in shows one confirmation dialog
for the whole batch and rolls back all writes if any operation fails.

## Smoke test

```powershell
$messages = @(
'{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0"}}}',
'{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
) -join "`n"

$messages | dotnet run --project .\src\RevitCodexBridge.Mcp\RevitCodexBridge.Mcp.csproj
```
