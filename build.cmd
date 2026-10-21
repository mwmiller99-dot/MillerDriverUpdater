@echo off
rem Builds dist\MillerDriverUpdater.exe with the C# compiler that ships inside Windows (.NET Framework 4.8),
rem then runs the tests. No Visual Studio, no SDK, no downloads.
rem   build.cmd           build + test
rem   build.cmd notest    build only
setlocal
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
  echo The .NET Framework 4 C# compiler was not found under %WINDIR%\Microsoft.NET.
  exit /b 1
)
set "WPF=%FW%\WPF"
set REFS=/r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" /r:"%FW%\Microsoft.CSharp.dll" /r:"%FW%\System.Management.dll" ^
 /r:"%FW%\System.Xml.dll" /r:"%FW%\System.Web.Extensions.dll" /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\System.Drawing.dll" ^
 /r:"%FW%\System.Windows.Forms.dll" /r:"%FW%\System.ServiceProcess.dll" /r:"%FW%\System.Xaml.dll" ^
 /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsBase.dll"

if not exist dist mkdir dist
echo Building dist\MillerDriverUpdater.exe ...
"%FW%\csc.exe" /nologo /noconfig /target:winexe /platform:anycpu /optimize+ /warn:4 /nowarn:1702 ^
  /out:dist\MillerDriverUpdater.exe /win32icon:assets\mdu.ico /win32manifest:app\app.manifest ^
  /resource:app\MainWindow.xaml,Mdu.MainWindow.xaml /resource:assets\mdu.ico,Mdu.mdu.ico ^
  %REFS% app\*.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo Built dist\MillerDriverUpdater.exe

if /i "%~1"=="notest" exit /b 0
if not exist build\tests mkdir build\tests
copy /y dist\MillerDriverUpdater.exe build\tests\ >nul
"%FW%\csc.exe" /nologo /noconfig /target:exe /platform:anycpu /out:build\tests\MduTests.exe ^
  /r:dist\MillerDriverUpdater.exe /r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" tests\Tests.cs
if errorlevel 1 (
  echo TEST BUILD FAILED
  exit /b 1
)
build\tests\MduTests.exe
exit /b %errorlevel%
