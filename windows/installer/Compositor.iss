#ifndef AppVersion
  #define AppVersion "1.4.9.1"
#endif
#ifndef SourceDir
  #error SourceDir must name the self-contained package folder
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{DA92A36E-0E54-4A33-A787-2632E6879BFA}
AppName=Compositor for Windows
AppVersion={#AppVersion}
AppVerName=Compositor for Windows {#AppVersion}
AppPublisher=Compositor Windows contributors
AppPublisherURL=https://github.com/konban47/Compositor
AppSupportURL=https://github.com/konban47/Compositor
DefaultDirName={autopf}\Compositor
DefaultGroupName=Compositor
UninstallDisplayIcon={app}\Compositor.exe
SetupIconFile=..\src\Compositor.Desktop\Assets\compositor.ico
LicenseFile={#SourceDir}\LICENSE.txt
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=Compositor-Windows-{#AppVersion}-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
english.RuntimeStartError=Could not start the Microsoft Visual C++ runtime installer.
chinesesimp.RuntimeStartError=无法启动 Microsoft Visual C++ 运行库安装程序。
english.RuntimeInstallError=Microsoft Visual C++ runtime installation failed. Error code: %1
chinesesimp.RuntimeInstallError=Microsoft Visual C++ 运行库安装失败。错误代码：%1

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\redist\vc_redist.x64.exe"; Flags: dontcopy

[Icons]
Name: "{group}\Compositor"; Filename: "{app}\Compositor.exe"
Name: "{group}\{cm:UninstallProgram,Compositor}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Compositor"; Filename: "{app}\Compositor.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Compositor.exe"; Description: "{cm:LaunchProgram,Compositor}"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  Code := -1;
  if FileExists(ExpandConstant('{sys}\vcruntime140_1.dll')) and
     FileExists(ExpandConstant('{sys}\msvcp140_1.dll')) then Exit;
  ExtractTemporaryFile('vc_redist.x64.exe');
  if not Exec(ExpandConstant('{tmp}\vc_redist.x64.exe'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := CustomMessage('RuntimeStartError');
  if (Result = '') and (Code <> 0) and (Code <> 1638) and (Code <> 3010) then
    Result := FmtMessage(CustomMessage('RuntimeInstallError'), [IntToStr(Code)]);
  if Code = 3010 then NeedsRestart := True;
end;
