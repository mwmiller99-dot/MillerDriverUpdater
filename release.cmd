@echo off
rem Publishes a new version: build + tests, Setup.exe, SHA256SUMS.txt, then a GitHub release that every installed copy
rem finds through its Update banner. One-time setup on this PC:
rem   winget install --id GitHub.cli --exact      then      gh auth login
rem Before each release: bump the version (Core.cs AppInfo.Version + Assembly*Version, app.manifest, APPVER in
rem installer\mdu.nsi) and write what changed into release-notes.md.
setlocal
cd /d "%~dp0"

for /f tokens^=2^ delims^=^" %%v in ('findstr /c:"const string Version =" app\Core.cs') do set "VER=%%v"
for /f tokens^=2^ delims^=^" %%r in ('findstr /c:"const string Repo =" app\Updater.cs') do set "REPO=%%r"
if "%REPO%"==";" set "REPO="
if "%VER%"=="" (
  echo Could not read the version from app\Core.cs.
  exit /b 1
)
if "%REPO%"=="" (
  echo No GitHub repository is set: put "owner/name" in Updater.Repo in app\Updater.cs first.
  exit /b 1
)
echo Releasing Miller Driver Updater %VER% to github.com/%REPO%

call build.cmd || exit /b 1
set "MAKENSIS=%ProgramFiles(x86)%\NSIS\makensis.exe"
if not exist "%MAKENSIS%" set "MAKENSIS=makensis.exe"
pushd installer
"%MAKENSIS%" /V2 /DAPPVER=%VER% mdu.nsi || (popd & exit /b 1)
popd
set "SETUP=dist\MillerDriverUpdater-%VER%-Setup.exe"
if not exist "%SETUP%" (
  echo %SETUP% was not built.
  exit /b 1
)

rem Checksums for people who want to verify by hand (the app itself uses the digest GitHub lists for the file).
> dist\SHA256SUMS.txt (
  for %%f in ("%SETUP%" dist\MillerDriverUpdater.exe) do (
    for /f "skip=1 tokens=* delims=" %%h in ('certutil -hashfile "%%~f" SHA256 ^| findstr /v /c:"CertUtil"') do echo %%h  %%~nxf
  )
)
type dist\SHA256SUMS.txt

where gh >nul 2>nul
if errorlevel 1 goto manual
gh auth status >nul 2>nul
if errorlevel 1 goto manual
set "NOTES=release-notes.md"
if not exist "%NOTES%" (
  echo Miller Driver Updater %VER%> "%TEMP%\mdu-notes.md"
  set "NOTES=%TEMP%\mdu-notes.md"
)
gh release create v%VER% "%SETUP%" dist\SHA256SUMS.txt --repo %REPO% --title "Miller Driver Updater %VER%" --notes-file "%NOTES%" || exit /b 1
echo.
echo Published: https://github.com/%REPO%/releases/tag/v%VER%
echo Installed copies offer the update the next time they open (and in the weekly check).
exit /b 0

:manual
echo.
echo GitHub CLI is not installed or not signed in, so the release was not published. Either:
echo   - winget install --id GitHub.cli --exact , then  gh auth login , then run release.cmd again; or
echo   - on https://github.com/%REPO%/releases/new : tag v%VER%, title "Miller Driver Updater %VER%",
echo     attach %SETUP% and dist\SHA256SUMS.txt, Publish release.
exit /b 2
