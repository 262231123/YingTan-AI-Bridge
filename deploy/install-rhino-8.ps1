param(
    [string] $Configuration = "Release",
    [string] $RhinoSystemDir = "C:\Program Files\Rhino 8\System"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\YingTanAiBridge.Rhino\YingTanAiBridge.Rhino.csproj"
$output = Join-Path $repoRoot "src\YingTanAiBridge.Rhino\bin\$Configuration\net7.0-windows"
$installDir = Join-Path $env:LOCALAPPDATA "YingTanAiBridge\Rhino\8"
$pluginPath = Join-Path $installDir "YingTanRhinoAiBridge.dll"
$pluginId = "a76223cc-1c1b-4d73-a3f6-7f86d65c1b26"
$registryRoot = "HKCU:\Software\McNeel\Rhinoceros\8.0\Plug-ins\$pluginId"

dotnet build $project -c $Configuration -p:RhinoSystemDir="$RhinoSystemDir"
if ($LASTEXITCODE -ne 0) {
    throw "Rhino 8 plug-in build failed."
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Get-ChildItem -LiteralPath $output -File |
    Where-Object { $_.Extension -in ".dll", ".json" } |
    Copy-Item -Destination $installDir -Force

New-Item -Path $registryRoot -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "Name" -Value "YingTan Rhino AI Bridge" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "EnglishName" -Value "YingTan Rhino AI Bridge" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "Description" -Value "AI chat, model query, and controlled automation for Rhino" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "Organization" -Value "YingTan Technology" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "WebSite" -Value "https://ytszkj.net" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "EMail" -Value "scott.xyc@foxmail.com" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "LoadMode" -Value 2 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "Type" -Value 16 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "IsDotNETPlugIn" -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "DirectoryInstall" -Value 0 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $registryRoot -Name "AddToHelpMenu" -Value 0 -PropertyType DWord -Force | Out-Null

$pluginKey = Join-Path $registryRoot "PlugIn"
New-Item -Path $pluginKey -Force | Out-Null
New-ItemProperty -Path $pluginKey -Name "FileName" -Value $pluginPath -PropertyType String -Force | Out-Null

$commandKey = Join-Path $registryRoot "CommandList"
New-Item -Path $commandKey -Force | Out-Null
New-ItemProperty -Path $commandKey -Name "YingTanAiBridge" -Value "2;YingTanAiBridge" -PropertyType String -Force | Out-Null

Write-Host "Installed Rhino 8 plug-in:"
Write-Host $pluginPath
Write-Host "Restart Rhino 8, then run YingTanAiBridge."
