#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PayloadDir
  #define PayloadDir "payload"
#endif
#ifndef OutputDir
  #define OutputDir "..\release"
#endif

[Setup]
AppId={{2C735464-D8DD-48E7-A9EC-6BBC90102E16}
AppName=盈碳 AI 工程助手
AppVersion={#AppVersion}
AppPublisher=YingTan Technology
AppPublisherURL=https://ytszkj.net
DefaultDirName={localappdata}\YingTanAiBridge
DefaultGroupName=盈碳 AI 工程助手
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=YingTan-AI-Bridge-v{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=盈碳 AI 工程助手
SetupLogging=yes

[Components]
Name: "revit"; Description: "Autodesk Revit"; Types: full compact custom; Flags: fixed
Name: "revit\2020"; Description: "Revit 2020"; Check: HasPayload('revit\2020') and IsRevitInstalled('2020')
Name: "revit\2021"; Description: "Revit 2021"; Check: HasPayload('revit\2021') and IsRevitInstalled('2021')
Name: "revit\2022"; Description: "Revit 2022"; Check: HasPayload('revit\2022') and IsRevitInstalled('2022')
Name: "revit\2023"; Description: "Revit 2023"; Check: HasPayload('revit\2023') and IsRevitInstalled('2023')
Name: "revit\2024"; Description: "Revit 2024"; Check: HasPayload('revit\2024') and IsRevitInstalled('2024')
Name: "revit\2025"; Description: "Revit 2025"; Check: HasPayload('revit\2025') and IsRevitInstalled('2025')
Name: "revit\2026"; Description: "Revit 2026"; Check: HasPayload('revit\2026') and IsRevitInstalled('2026')
Name: "revit\2027"; Description: "Revit 2027"; Check: HasPayload('revit\2027') and IsRevitInstalled('2027')
Name: "rhino"; Description: "McNeel Rhino"; Types: full custom; Flags: fixed
Name: "rhino\7"; Description: "Rhino 7"; Check: HasPayload('rhino\7') and IsRhinoInstalled('7')
Name: "rhino\8"; Description: "Rhino 8"; Check: HasPayload('rhino\8') and IsRhinoInstalled('8')
Name: "autocad"; Description: "Autodesk AutoCAD"; Types: full custom; Flags: fixed
Name: "autocad\2022"; Description: "AutoCAD 2022"; Check: HasPayload('autocad\2022') and IsAutoCadInstalled('2022')
Name: "autocad\2023"; Description: "AutoCAD 2023"; Check: HasPayload('autocad\2023') and IsAutoCadInstalled('2023')
Name: "autocad\2024"; Description: "AutoCAD 2024"; Check: HasPayload('autocad\2024') and IsAutoCadInstalled('2024')
Name: "autocad\2025"; Description: "AutoCAD 2025"; Check: HasPayload('autocad\2025') and IsAutoCadInstalled('2025')
Name: "autocad\2026"; Description: "AutoCAD 2026"; Check: HasPayload('autocad\2026') and IsAutoCadInstalled('2026')
Name: "autocad\2027"; Description: "AutoCAD 2027"; Check: HasPayload('autocad\2027') and IsAutoCadInstalled('2027')
Name: "inventor"; Description: "Autodesk Inventor（当前源码尚未产出可安装 AddIn）"; Types: custom; Flags: fixed; Check: HasAnyInventorPayload
Name: "inventor\2022"; Description: "Inventor 2022"; Check: HasPayload('inventor\2022') and IsInventorInstalled('2022')
Name: "inventor\2023"; Description: "Inventor 2023"; Check: HasPayload('inventor\2023') and IsInventorInstalled('2023')
Name: "inventor\2024"; Description: "Inventor 2024"; Check: HasPayload('inventor\2024') and IsInventorInstalled('2024')
Name: "inventor\2025"; Description: "Inventor 2025"; Check: HasPayload('inventor\2025') and IsInventorInstalled('2025')
Name: "inventor\2026"; Description: "Inventor 2026"; Check: HasPayload('inventor\2026') and IsInventorInstalled('2026')
Name: "inventor\2027"; Description: "Inventor 2027"; Check: HasPayload('inventor\2027') and IsInventorInstalled('2027')

[Files]
Source: "{#PayloadDir}\revit\2020\*"; DestDir: "{localappdata}\YingTanAiBridge\Revit\2020"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: revit\2020
Source: "{#PayloadDir}\revit\2021\*"; DestDir: "{localappdata}\YingTanAiBridge\Revit\2021"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: revit\2021
Source: "{#PayloadDir}\revit\2022\*"; DestDir: "{localappdata}\YingTanAiBridge\Revit\2022"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: revit\2022
Source: "{#PayloadDir}\revit\2023\*"; DestDir: "{localappdata}\YingTanAiBridge\Revit\2023"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: revit\2023
Source: "{#PayloadDir}\revit\2024\*"; DestDir: "{localappdata}\YingTanAiBridge\Revit\2024"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: revit\2024
Source: "{#PayloadDir}\revit\2025\*"; DestDir: "{localappdata}\YingTanAiBridge\Revit\2025"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: revit\2025
Source: "{#PayloadDir}\revit\2026\*"; DestDir: "{localappdata}\YingTanAiBridge\Revit\2026"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: revit\2026
Source: "{#PayloadDir}\revit\2027\*"; DestDir: "{localappdata}\YingTanAiBridge\Revit\2027"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: revit\2027
Source: "{#PayloadDir}\rhino\7\*"; DestDir: "{localappdata}\YingTanAiBridge\Rhino\7"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: rhino\7
Source: "{#PayloadDir}\rhino\8\*"; DestDir: "{localappdata}\YingTanAiBridge\Rhino\8"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: rhino\8
Source: "{#PayloadDir}\autocad\2022\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\YingTan.AutoCAD.AiBridge.bundle"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: autocad\2022
Source: "{#PayloadDir}\autocad\2023\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\YingTan.AutoCAD.AiBridge.bundle"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: autocad\2023
Source: "{#PayloadDir}\autocad\2024\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\YingTan.AutoCAD.AiBridge.bundle"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: autocad\2024
Source: "{#PayloadDir}\autocad\2025\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\YingTan.AutoCAD.AiBridge.bundle"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: autocad\2025
Source: "{#PayloadDir}\autocad\2026\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\YingTan.AutoCAD.AiBridge.bundle"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: autocad\2026
Source: "{#PayloadDir}\autocad\2027\*"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\YingTan.AutoCAD.AiBridge.bundle"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: autocad\2027
Source: "{#PayloadDir}\inventor\*"; DestDir: "{localappdata}\YingTanAiBridge\Inventor"; Flags: recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Components: inventor

[Registry]
Root: HKCU; Subkey: "Software\McNeel\Rhinoceros\7.0\Plug-ins\a76223cc-1c1b-4d73-a3f6-7f86d65c1b26"; ValueType: string; ValueName: "Name"; ValueData: "YingTan Rhino AI Bridge"; Components: rhino\7; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\McNeel\Rhinoceros\7.0\Plug-ins\a76223cc-1c1b-4d73-a3f6-7f86d65c1b26\PlugIn"; ValueType: string; ValueName: "FileName"; ValueData: "{localappdata}\YingTanAiBridge\Rhino\7\YingTanRhinoAiBridge.dll"; Components: rhino\7
Root: HKCU; Subkey: "Software\McNeel\Rhinoceros\8.0\Plug-ins\a76223cc-1c1b-4d73-a3f6-7f86d65c1b26"; ValueType: string; ValueName: "Name"; ValueData: "YingTan Rhino AI Bridge"; Components: rhino\8; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\McNeel\Rhinoceros\8.0\Plug-ins\a76223cc-1c1b-4d73-a3f6-7f86d65c1b26\PlugIn"; ValueType: string; ValueName: "FileName"; ValueData: "{localappdata}\YingTanAiBridge\Rhino\8\YingTanRhinoAiBridge.dll"; Components: rhino\8

[Code]
function HasPayload(RelativePath: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{#PayloadDir}\' + RelativePath));
end;

function IsRevitInstalled(Version: String): Boolean;
begin
  Result := FileExists(ExpandConstant('{autopf}\Autodesk\Revit ' + Version + '\Revit.exe')) or
            RegKeyExists(HKLM64, 'SOFTWARE\Autodesk\Revit\Autodesk Revit ' + Version);
end;

function IsRhinoInstalled(Version: String): Boolean;
begin
  Result := FileExists(ExpandConstant('{autopf}\Rhino ' + Version + '\System\Rhino.exe')) or
            RegKeyExists(HKLM64, 'SOFTWARE\McNeel\Rhinoceros\' + Version + '.0');
end;

function IsAutoCadInstalled(Version: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{autopf}\Autodesk\AutoCAD ' + Version)) or
            RegKeyExists(HKLM64, 'SOFTWARE\Autodesk\AutoCAD');
end;

function IsInventorInstalled(Version: String): Boolean;
begin
  Result := FileExists(ExpandConstant('{autopf}\Autodesk\Inventor ' + Version + '\Bin\Inventor.exe'));
end;

function HasAnyInventorPayload: Boolean;
begin
  Result := HasPayload('inventor');
end;

procedure WriteRevitManifest(Version: String);
var
  ManifestDir, ManifestPath, AssemblyPath, Xml: String;
begin
  if not WizardIsComponentSelected('revit\' + Version) then Exit;
  ManifestDir := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' + Version);
  ForceDirectories(ManifestDir);
  ManifestPath := ManifestDir + '\RevitCodexBridge.addin';
  AssemblyPath := ExpandConstant('{localappdata}\YingTanAiBridge\Revit\' + Version + '\RevitCodexBridge.Addin.dll');
  Xml := '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
    '<RevitAddIns><AddIn Type="Application"><Name>YingTan Revit AI Bridge</Name><Assembly>' + AssemblyPath +
    '</Assembly><AddInId>79D95BD4-2D50-4473-A16E-34B02FA0D692</AddInId><FullClassName>RevitCodexBridge.Addin.BridgeApplication</FullClassName><VendorId>CODX</VendorId></AddIn></RevitAddIns>';
  SaveStringToFile(ManifestPath, Xml, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var V: Integer;
begin
  if CurStep = ssPostInstall then
    for V := 2020 to 2027 do WriteRevitManifest(IntToStr(V));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var V: Integer;
begin
  if CurUninstallStep = usUninstall then
    for V := 2020 to 2027 do
      DeleteFile(ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' + IntToStr(V) + '\RevitCodexBridge.addin'));
end;
