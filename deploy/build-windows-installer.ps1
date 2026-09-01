param(
    [string] $Version = "0.1.0",
    [string] $OutputDir = "",
    [string] $IsccPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$payloadRoot = Join-Path $repoRoot "installer\payload"
if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path $repoRoot "release" }
if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($IsccPath)) { throw "Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup" }

if (Test-Path -LiteralPath $payloadRoot) { Remove-Item -LiteralPath $payloadRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payloadRoot, $OutputDir | Out-Null

$built = [System.Collections.Generic.List[string]]::new()
foreach ($revitVersion in 2020..2027) {
    $revitDir = "C:\Program Files\Autodesk\Revit $revitVersion"
    if (-not (Test-Path -LiteralPath (Join-Path $revitDir "RevitAPI.dll"))) { continue }
    & dotnet publish (Join-Path $repoRoot "src\RevitCodexBridge.Addin\RevitCodexBridge.Addin.csproj") -c Release --no-self-contained "-p:RevitVersion=$revitVersion" "-p:RevitInstallDir=$revitDir" "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw "Revit $revitVersion build failed." }
    $framework = if ($revitVersion -le 2024) { "net48" } elseif ($revitVersion -eq 2025) { "net8.0-windows" } else { "net10.0-windows" }
    $source = Join-Path $repoRoot "src\RevitCodexBridge.Addin\bin\Release\$framework\Revit$revitVersion\publish"
    $target = Join-Path $payloadRoot "revit\$revitVersion"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $target -Recurse -Force
    $built.Add("Revit $revitVersion")
}

foreach ($rhinoVersion in 7, 8) {
    $systemDir = "C:\Program Files\Rhino $rhinoVersion\System"
    if (-not (Test-Path -LiteralPath (Join-Path $systemDir "RhinoCommon.dll"))) { continue }
    & dotnet build (Join-Path $repoRoot "src\YingTanAiBridge.Rhino\YingTanAiBridge.Rhino.csproj") -c Release "-p:RhinoSystemDir=$systemDir"
    if ($LASTEXITCODE -ne 0) { throw "Rhino $rhinoVersion build failed." }
    $target = Join-Path $payloadRoot "rhino\$rhinoVersion"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem (Join-Path $repoRoot "src\YingTanAiBridge.Rhino\bin\Release\net7.0-windows") -File | Where-Object Extension -in '.dll','.json' | Copy-Item -Destination $target -Force
    $built.Add("Rhino $rhinoVersion")
}

foreach ($autoCadVersion in 2022..2027) {
    $autoCadDir = "C:\Program Files\Autodesk\AutoCAD $autoCadVersion"
    if (-not (Test-Path -LiteralPath (Join-Path $autoCadDir "AcMgd.dll"))) { continue }
    & dotnet build (Join-Path $repoRoot "src\YingTanAiBridge.AutoCAD\YingTanAiBridge.AutoCAD.csproj") -c Release -p:EnableAutoCadSdk=true "-p:AutoCadInstallDir=$autoCadDir"
    if ($LASTEXITCODE -ne 0) { throw "AutoCAD $autoCadVersion build failed." }
    $target = Join-Path $payloadRoot "autocad\$autoCadVersion\Contents\Windows"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem (Join-Path $repoRoot "src\YingTanAiBridge.AutoCAD\bin\Release\net48") -Filter '*.dll' | Copy-Item -Destination $target -Force
    $seriesByVersion = @{ 2022 = 'R24.1'; 2023 = 'R24.2'; 2024 = 'R24.3'; 2025 = 'R25.0'; 2026 = 'R25.1'; 2027 = 'R25.2' }
    $manifestPath = Join-Path (Split-Path -Parent (Split-Path -Parent $target)) "PackageContents.xml"
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "AutoCAD2022.PackageContents.xml")
    $manifest = $manifest.Replace('SeriesMin="R24.1"', "SeriesMin=`"$($seriesByVersion[$autoCadVersion])`"")
    $manifest = $manifest.Replace('SeriesMax="R24.1"', "SeriesMax=`"$($seriesByVersion[$autoCadVersion])`"")
    $manifest = $manifest.Replace('AppVersion="1.0.0"', "AppVersion=`"$Version`"")
    Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8
    $built.Add("AutoCAD $autoCadVersion")
}

if ($built.Count -eq 0) { throw "No supported host payload could be built on this computer." }

& $IsccPath "/DAppVersion=$Version" "/DPayloadDir=$payloadRoot" "/DOutputDir=$OutputDir" (Join-Path $repoRoot "installer\YingTanAiBridge.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

$setup = Join-Path $OutputDir "YingTan-AI-Bridge-v$Version-Setup.exe"
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath "$setup.sha256" -Encoding ASCII
Write-Host "Built payloads: $($built -join ', ')"
Write-Host "Installer: $setup"
Write-Host "SHA256: $hash"
