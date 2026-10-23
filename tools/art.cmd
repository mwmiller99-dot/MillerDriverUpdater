@echo off
rem Redraws the icon, installer pictures and GitHub preview from tools\ArtGen.cs. Run it after changing the design,
rem then build.cmd (the icon is compiled into the exe).
setlocal
cd /d "%~dp0.."
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "WPF=%FW%\WPF"
if not exist build mkdir build
"%FW%\csc.exe" /nologo /noconfig /target:exe /out:build\ArtGen.exe ^
  /r:"%FW%\System.dll" /r:"%FW%\System.Xaml.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsBase.dll" ^
  tools\ArtGen.cs || exit /b 1
build\ArtGen.exe
