@echo off
rem Redraws the README screenshots from the built app with sample data (tools\ScreenshotGen.cs). Run build.cmd first.
setlocal
cd /d "%~dp0.."
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "WPF=%FW%\WPF"
if not exist dist\MillerDriverUpdater.exe (
  echo Run build.cmd first.
  exit /b 1
)
if not exist build\shots mkdir build\shots
copy /y dist\MillerDriverUpdater.exe build\shots\ >nul
"%FW%\csc.exe" /nologo /noconfig /target:exe /out:build\shots\ScreenshotGen.exe /r:dist\MillerDriverUpdater.exe ^
  /r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" /r:"%FW%\System.Xaml.dll" ^
  /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsBase.dll" ^
  tools\ScreenshotGen.cs || exit /b 1
echo Drawing the screenshots:
build\shots\ScreenshotGen.exe light || exit /b 1
build\shots\ScreenshotGen.exe dark
