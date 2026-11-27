; vView setup (Inno Setup 6), like vXplorer's. Packages the dist\vView folder - build it first with build.cmd (build.cmd setup also runs this).
;
; Make the setup:  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\vView.iss
; It lands in dist\setup\vView-setup.exe - the same name in every release, so a web page can link to the latest (the version is read from dist\vView\vView.exe).
;
; Installs for the current user only (no admin): %LOCALAPPDATA%\Programs\vView. Settings and the log stay in %APPDATA%\vView.
; vView's updater replaces this folder as a whole (Updater.cs: vView.update -> vView, the old one kept as vView.old until the new one
; starts), copying the uninstaller (unins000.*) across.

#define SourceDir "..\dist\vView"
#define SourceExe SourceDir + "\vView.exe"
#if !FileExists(SourceExe)
  #error dist\vView\vView.exe is missing - run build.cmd first
#endif
; "0.1.1.0" -> "0.1.1"
#define AppVer RemoveFileExt(GetVersionNumbersString(SourceExe))

[Setup]
; signed with the Certum certificate when build.cmd signs (/DSIGN /Svsuite=...sign.cmd): the setup and its uninstaller
#ifdef SIGN
SignTool=vsuite
#endif
AppId={{4E8B2D6A-7C31-4F9A-9E52-B1D3A6C8F072}
AppName=vView
AppVersion={#AppVer}
AppVerName=vView {#AppVer}
AppPublisher=Steven Van Teylingen
VersionInfoVersion={#AppVer}
DefaultDirName={autopf}\vView
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; every running vView holds this (Program.cs): setup and uninstall ask you to close it first
AppMutex=vView.Running
SetupIconFile=..\vview.ico
UninstallDisplayIcon={app}\vView.exe
UninstallDisplayName=vView
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist\setup
OutputBaseFilename=vView-setup
UsePreviousTasks=yes
ChangesAssociations=yes

[Messages]
SetupAppRunningError=vView is open. Close its windows, then click OK to carry on.
UninstallAppRunningError=vView is open. Close its windows, then click OK to carry on.

[Tasks]
Name: "openwith"; Description: "Offer vView in ""Open with"" and in Settings > Default apps for pictures, videos and sounds (it doesn't take any over - Options > General > Default app does that)"
Name: "desktopicon"; Description: "A shortcut on the desktop"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\vView"; Filename: "{app}\vView.exe"
Name: "{autodesktop}\vView"; Filename: "{app}\vView.exe"; Tasks: desktopicon

[Run]
; vView signs itself up for every type it opens (Core\DefaultApp.cs - the same as Options > General > Default app)
Filename: "{app}\vView.exe"; Parameters: "--register-types"; Flags: runhidden waituntilterminated; Tasks: openwith
Filename: "{app}\vView.exe"; Description: "Start vView now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\vView.exe"; Parameters: "--unregister-types"; Flags: runhidden waituntilterminated; RunOnceId: "unregtypes"

[UninstallDelete]
; left by the updater beside the install folder
Type: filesandordirs; Name: "{app}.old"
Type: filesandordirs; Name: "{app}.failed"
Type: filesandordirs; Name: "{app}.update"
Type: dirifempty; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() and DirExists(ExpandConstant('{userappdata}\vView')) then
  begin
    if MsgBox('Also delete your vView settings (options, recent streams) and log?' + #13#10 + #13#10 +
              ExpandConstant('{userappdata}\vView') + #13#10 + #13#10 +
              'Keep them (No) if you might install vView again - it picks up where you left off.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\vView'), True, True, True);
  end;
end;