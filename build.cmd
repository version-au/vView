@echo off
rem Double-click to build a release. Needs the .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0).
rem Produces dist\vView\ (self-contained: vView.exe, .NET and VLC's engine in libvlc\ - nothing else to install), zipped as dist\vView.zip,
rem and dist\setup\vView-setup.exe (Inno Setup 6, installer\vView.iss). dist\site\ = the update site's files (vsuite.vrsion.com - copy it
rem over the site's root; "build.cmd pre" = a pre-release, in vview\pre\). Nothing goes to GitHub any more (the bridge release was 0.1.17).
rem VLC's DLLs must stay separate files (LGPL), so vView isn't one exe.
rem With VLC installed, its vlc-cache-gen writes libvlc\win-x64\plugins\plugins.dat (see vView.csproj) - VLC then starts in a blink.
cd /d "%~dp0"
rem the installer is in every release (vShell's Settings > Updates > vSuite apps installs from it); build.cmd nosetup skips it
set WANTSETUP=1
for %%a in (%*) do if /i "%%~a"=="nosetup" set WANTSETUP=
dotnet --version >nul 2>&1 || (echo .NET SDK not found. Install .NET 10 SDK first. & pause & exit /b 1)
rem the newest vSuite.Core library first (the submodule, committed if it moved) - vSuite.Core\scripts\suite-core.ps1
if not exist vSuite.Core\scripts\suite-core.ps1 git submodule update --init vSuite.Core
powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\suite-core.ps1"
if errorlevel 1 (echo. & echo BUILD STOPPED - vSuite.Core, see above. & pause & exit /b 1)
rem the shared library (vSuite.Core) must be its latest version, committed in vView - a release never ships an old one
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\check-library.ps1"
if errorlevel 1 (echo. & pause & exit /b 1)
if exist dist\vView rmdir /s /q dist\vView
dotnet publish vView.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none -o dist\vView
if errorlevel 1 (echo. & echo BUILD FAILED - copy the errors above and send them to me. & pause & exit /b 1)
copy /y THIRD-PARTY.txt dist\vView\ >nul
if not exist dist\vView\libvlc\win-x64\plugins\plugins.dat echo NOTE: no VLC plugin cache (install VLC to make one) - videos take a moment longer to start the first time.
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Select-Xml -Path vView.csproj -XPath '//Version').Node.InnerText"`) do set VER=%%v
rem code signing (the Certum certificate, through SimplySign Desktop - connect it first): the app now, so the installer carries the
rem signed one; Inno Setup signs the setup and its uninstaller (sign.cmd). "nosign" skips it; an upload needs it. vSuite.Core\scripts\sign.ps1
set SIGN=1
for %%a in (%*) do if /i "%%~a"=="nosign" set SIGN=
set SIGNREQ=
for %%a in (%*) do if /i "%%~a"=="upload" set SIGNREQ=-Require
if defined SIGN powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\sign.ps1" %SIGNREQ% dist\vView\vView.exe dist\vView\vView.dll dist\vView\vSuite.Core.dll dist\vView\vSuite.Shell.dll
if defined SIGN if errorlevel 2 (set SIGN=) else if errorlevel 1 (echo. & echo SIGNING FAILED - see above. & pause & exit /b 1)
del /q dist\vView.zip 2>nul
powershell -NoProfile -Command "Compress-Archive -Path 'dist\vView\*' -DestinationPath 'dist\vView.zip' -CompressionLevel Optimal"
echo Built: %~dp0dist\vView\vView.exe
set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
rem an older installer is never put up with this version: dist\setup is emptied first
del /q dist\setup\*.exe 2>nul
if not defined WANTSETUP (echo No installer this time - nosetup.) else if not exist "%ISCC%" (echo Inno Setup 6 not found - no installer made.) else (
  if defined SIGN ("%ISCC%" /Q /DSIGN "/Svsuite=$q%~dp0vSuite.Core\scripts\sign.cmd$q $f" installer\vView.iss) else ("%ISCC%" /Q installer\vView.iss)
  if errorlevel 1 (echo. & echo INSTALLER FAILED - see above. & pause & exit /b 1)
  echo Installer: %~dp0dist\setup\
)
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\release-notes.ps1"
rem the update site's files (vView.zip, installer, latest.json, changelog.md) in dist\site\vview\ - build.cmd pre: dist\site\vview\pre\
set SITEPRE=
for %%a in (%*) do if /i "%%~a"=="pre" set SITEPRE=-Pre
if exist dist\site rmdir /s /q dist\site
powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\site-release.ps1" -App vView -Version %VER% -Install dist\vView.zip -Installer dist\setup\vView-setup.exe -Notes dist\release-notes.md -Changelog CHANGELOG.md -Out dist\site %SITEPRE%
if errorlevel 1 (echo. & echo UPDATE SITE FILES FAILED - see above. & pause & exit /b 1)
rem "build.cmd upload" (with "pre" for a pre-release): dist\site\ straight onto the update site - SFTP with the vsuite-site key
rem (%USERPROFILE%\.ssh\config), latest.json last, then checked over https (vSuite.Core\scripts\site-upload.ps1)
set UPLOAD=
for %%a in (%*) do if /i "%%~a"=="upload" set UPLOAD=1
if defined UPLOAD if not defined SIGN (echo. & echo UPLOAD STOPPED - this build isn't signed. Connect SimplySign Desktop and build again. & pause & exit /b 1)
if defined UPLOAD powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\site-upload.ps1" -Site dist\site
if defined UPLOAD if errorlevel 1 (echo. & echo UPLOAD FAILED - see above. & pause & exit /b 1)
echo For the update site: copy everything in %~dp0dist\site\ over the site's root (the files there are replaced).
set PAUSEIT=1
for %%a in (%*) do if /i "%%~a"=="nopause" set PAUSEIT=
if defined PAUSEIT pause
