param(
    [string] $Version = "0.1.0",
    [string] $RevitVersion = "2027",
    [string] $RevitInstallDir = "",
    [string] $OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "release"
}

$targetFramework = if ($RevitVersion -in @("2020", "2021", "2022", "2023", "2024")) {
    "net48"
}
elseif ($RevitVersion -eq "2025") {
    "net8.0-windows"
}
else {
    "net10.0-windows"
}

$buildArgs = @(
    "publish",
    (Join-Path $repoRoot "src\RevitCodexBridge.Addin\RevitCodexBridge.Addin.csproj"),
    "--configuration", "Release",
    "--no-self-contained",
    "-p:RevitVersion=$RevitVersion",
    "-p:Version=$Version"
)
if (-not [string]::IsNullOrWhiteSpace($RevitInstallDir)) {
    $buildArgs += "-p:RevitInstallDir=$RevitInstallDir"
}

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$publishDir = Join-Path $repoRoot "src\RevitCodexBridge.Addin\bin\Release\$targetFramework\Revit$RevitVersion\publish"
if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Publish output was not found: $publishDir"
}

$packageName = "YingTan-Revit-AI-Bridge-v$Version-Revit$RevitVersion"
$stagingDir = Join-Path $OutputDir $packageName
$zipPath = Join-Path $OutputDir "$packageName.zip"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Test-Path -LiteralPath $stagingDir) { Remove-Item -LiteralPath $stagingDir -Recurse -Force }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stagingDir "plugin") | Out-Null

Copy-Item -Path (Join-Path $publishDir "*") -Destination (Join-Path $stagingDir "plugin") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install-addin.ps1") -Destination $stagingDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall-addin.ps1") -Destination $stagingDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "RevitCodexBridge.addin.template") -Destination $stagingDir
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\usage-guide.md") -Destination (Join-Path $stagingDir "USAGE.md")

@"
# 盈碳 Revit AI Bridge $Version (Revit $RevitVersion)

1. 解压本安装包。
2. 在 PowerShell 中运行：

   ``powershell
   .\install-addin.ps1 -RevitVersion $RevitVersion -AssemblyPath .\plugin\RevitCodexBridge.Addin.dll
   ``

3. 重启 Revit，在“盈碳AI”面板中打开插件。

卸载：``.\uninstall-addin.ps1 -RevitVersion $RevitVersion``

详细用法见 ``USAGE.md``。请仅从你信任的 GitHub Release 下载本包。
"@ | Set-Content -LiteralPath (Join-Path $stagingDir "INSTALL.md") -Encoding UTF8

Compress-Archive -LiteralPath $stagingDir -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ASCII

Write-Host "Release package: $zipPath"
Write-Host "SHA256: $hash"
