param(
    [string] $Configuration = "Release",
    [string] $AutoCadInstallDir = "D:\Program Files\Autodesk\AutoCAD 2022"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\YingTanAiBridge.AutoCAD\YingTanAiBridge.AutoCAD.csproj"
$output = Join-Path $repoRoot "src\YingTanAiBridge.AutoCAD\bin\$Configuration\net48"
$pluginsRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$bundle = Join-Path $pluginsRoot "YingTan.AutoCAD.AiBridge.bundle"
$contents = Join-Path $bundle "Contents\Windows"

dotnet build $project -c $Configuration `
    -p:EnableAutoCadSdk=true `
    -p:AutoCadInstallDir="$AutoCadInstallDir"
if ($LASTEXITCODE -ne 0) {
    throw "AutoCAD 2022 plug-in build failed."
}

New-Item -ItemType Directory -Force -Path $contents | Out-Null
Get-ChildItem -LiteralPath $output -Filter "*.dll" |
    Copy-Item -Destination $contents -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "AutoCAD2022.PackageContents.xml") `
    -Destination (Join-Path $bundle "PackageContents.xml") `
    -Force

Write-Host "Installed AutoCAD 2022 bundle:"
Write-Host $bundle
Write-Host "Restart AutoCAD 2022, then run YINGTANAIBRIDGE."
