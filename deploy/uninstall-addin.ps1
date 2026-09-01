param(
    [string] $RevitVersion = "2027",
    [string] $AddinsDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AddinsDir)) {
    $AddinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
}

$manifestPath = Join-Path $AddinsDir "RevitCodexBridge.addin"
$installDir = Join-Path $env:LOCALAPPDATA "YingTanAiBridge\Revit\$RevitVersion"

if (Test-Path -LiteralPath $manifestPath) {
    Remove-Item -LiteralPath $manifestPath -Force
    Write-Host "Removed manifest: $manifestPath"
}

if (Test-Path -LiteralPath $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
    Write-Host "Removed plug-in files: $installDir"
}

Write-Host "Revit $RevitVersion add-in uninstalled. Restart Revit if it is running."
