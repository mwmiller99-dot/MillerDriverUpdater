@echo off
rem Publishes a new version.
rem   release.cmd         checks the build here, then tags vX.Y.Z and pushes the tag: GitHub Actions
rem                       (.github\workflows\release.yml) builds it on a clean Windows server, records a build
rem                       provenance attestation and publishes the release. Recommended.
rem   release.cmd local   builds and publishes from this PC instead (needs NSIS and the GitHub CLI signed in).
rem Before each release: bump the version (Core.cs AppInfo.Version + Assembly*Version, app.manifest, APPVER in
rem installer\mdu.nsi), write what changed into release-notes.md and CHANGELOG.md, and commit.
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
if /i "%~1"=="local" goto local

echo Releasing Miller Driver Updater %VER% through GitHub Actions (github.com/%REPO%)
rem By full path: cmd may be set not to look in the current folder for commands (NoDefaultCurrentDirectoryInExePath).
call "%~dp0build.cmd" || exit /b 1
git diff --quiet && git diff --cached --quiet || (
  echo Commit your changes first: the release is built from what is on GitHub.
  exit /b 1
)
git rev-parse -q --verify "refs/tags/v%VER%" >nul && (
  echo Tag v%VER% already exists. Bump the version first.
  exit /b 1
)
git tag -a "v%VER%" -m "Miller Driver Updater %VER%" || exit /b 1
git push origin HEAD "v%VER%" || (
  git tag -d "v%VER%" >nul
  exit /b 1
)
echo.
echo Tagged v%VER% and pushed it. GitHub is building, attesting and publishing it now (a few minutes):
echo   https://github.com/%REPO%/actions
echo   https://github.com/%REPO%/releases/tag/v%VER%
exit /b 0

:local
echo Releasing Miller Driver Updater %VER% from this PC to github.com/%REPO%
call "%~dp0build.cmd" || exit /b 1
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
gh release create v%VER% "%SETUP%" dist\SHA256SUMS.txt --repo %REPO% --title "Miller Driver Updater %VER%" --notes-file release-notes.md || exit /b 1
echo.
echo Published: https://github.com/%REPO%/releases/tag/v%VER%
exit /b 0

:manual
echo.
echo GitHub CLI is not installed or not signed in, so the release was not published. Either:
echo   - winget install --id GitHub.cli --exact , then  gh auth login , then run release.cmd local again; or
echo   - on https://github.com/%REPO%/releases/new : tag v%VER%, title "Miller Driver Updater %VER%",
echo     attach %SETUP% and dist\SHA256SUMS.txt, Publish release.
exit /b 2
