param(
    [string] $Configuration = "Release",
    [string] $RevitVersion = "2027",
    [string] $AssemblyPath = "",
    [string] $AddinsDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

if ($RevitVersion -in @("2020", "2021", "2022", "2023", "2024")) {
    $targetFramework = "net48"
}
elseif ($RevitVersion -eq "2025") {
    $targetFramework = "net8.0-windows"
}
else {
    $targetFramework = "net10.0-windows"
}

if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $repoRoot "src\RevitCodexBridge.Addin\bin\$Configuration\$targetFramework\Revit$RevitVersion\RevitCodexBridge.Addin.dll"
}

if (-not (Test-Path -LiteralPath $AssemblyPath)) {
    throw "Add-in assembly was not found: $AssemblyPath"
}

$sourceDir = Split-Path -Parent $AssemblyPath
$installDir = Join-Path $env:LOCALAPPDATA "YingTanAiBridge\Revit\$RevitVersion"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Get-ChildItem -LiteralPath $sourceDir -File |
    Copy-Item -Destination $installDir -Force
$AssemblyPath = Join-Path $installDir "RevitCodexBridge.Addin.dll"

if ([string]::IsNullOrWhiteSpace($AddinsDir)) {
    $AddinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
}

New-Item -ItemType Directory -Force -Path $AddinsDir | Out-Null

$templatePath = Join-Path $PSScriptRoot "RevitCodexBridge.addin.template"
$manifestPath = Join-Path $AddinsDir "RevitCodexBridge.addin"
$manifest = Get-Content -Raw -LiteralPath $templatePath
$manifest = $manifest.Replace("{{ASSEMBLY_PATH}}", $AssemblyPath)
Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8

Write-Host "Installed Revit add-in manifest:"
Write-Host $manifestPath
Write-Host "Installed assembly:"
Write-Host $AssemblyPath
Write-Host ""
Write-Host "Restart Revit $RevitVersion to load the bridge."
