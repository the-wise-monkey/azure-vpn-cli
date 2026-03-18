#ifndef MyAppVersion
  #define MyAppVersion "0.0.1"
#endif

[Setup]
AppId={{B7E3F2A1-8C4D-4F6E-9A2B-1D5E7F8C3A9B}
AppName=AzureVPN-CLI
AppVersion={#MyAppVersion}
AppPublisher=The Wise Monkey
AppPublisherURL=https://github.com/the-wise-monkey/azure-vpn-cli
DefaultDirName={userappdata}\AzureVPN-CLI
DisableProgramGroupPage=yes
OutputBaseFilename=AzureVPN-CLI-{#MyAppVersion}-setup
OutputDir=..\Output
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
ChangesEnvironment=yes
UninstallDisplayName=AzureVPN-CLI

[Files]
Source: "..\src\bin\Release\net48\vpn.exe"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
  ValueData: "{olddata};{app}"; Check: NeedsAddPath(ExpandConstant('{app}'))

[Code]
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Param + ';', ';' + OrigPath + ';') = 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Path, AppDir: string;
  P: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDir := ExpandConstant('{app}');
    if RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Path) then
    begin
      P := Pos(';' + AppDir, Path);
      if P > 0 then
      begin
        Delete(Path, P, Length(';' + AppDir));
        RegWriteStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Path);
      end;
    end;
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
