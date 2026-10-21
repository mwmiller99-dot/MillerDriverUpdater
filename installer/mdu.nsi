; Miller Driver Updater - Windows installer (NSIS 3). Build after build.cmd, from this folder:  makensis mdu.nsi
; Per-user install (no administrator rights): %LOCALAPPDATA%\Programs\Miller Driver Updater\MillerDriverUpdater.exe,
; Start menu + optional desktop shortcut, listed in Settings > Apps with an uninstaller. It replaces the app's earlier
; identity, "Miller Driver Update" (1.x PowerShell and 2.0): program files, shortcuts, uninstall entry and weekly
; check move over; reports and driver backups are kept (the app moves 2.0's data folder on its first run).

Unicode True
; The Windows build of NSIS (winget NSIS.NSIS) ships only x86 stubs; a 32-bit setup is fine here (it just copies the
; AnyCPU exe into the user's folder, and the app itself runs 64-bit). Builds that have amd64 stubs use them.
!if /FileExists "${NSISDIR}\Stubs\zlib-amd64-unicode"
  Target amd64-unicode
!else
  Target x86-unicode
!endif

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!define APPNAME   "Miller Driver Updater"
!ifndef APPVER
  !define APPVER  "2.2.0"
!endif
!define APPID     "MillerDriverUpdater"
!define APPEXE    "MillerDriverUpdater.exe"
!define TASKNAME  "Miller Driver Updater - weekly check"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPID}"
!define LNKDESC   "Update drivers and apps from official sources"

; The earlier name (1.x and 2.0).
!define OLDNAME   "Miller Driver Update"
!define OLDID     "MillerDriverUpdate"
!define OLDDIR    "$LOCALAPPDATA\Programs\Miller Driver Update"
!define OLDTASK   "Miller Driver Update - weekly check"

Name "${APPNAME}"
OutFile "..\dist\MillerDriverUpdater-${APPVER}-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
InstallDirRegKey HKCU "Software\${APPID}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APPNAME} ${APPVER}"
ShowInstDetails nevershow
ShowUninstDetails nevershow

VIProductVersion "${APPVER}.0"
VIAddVersionKey "ProductName"     "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "FileVersion"     "${APPVER}"
VIAddVersionKey "ProductVersion"  "${APPVER}"
VIAddVersionKey "CompanyName"     "${APPNAME}"
VIAddVersionKey "LegalCopyright"  "Official sources only: Windows Update, nvidia.com, winget."

; ---- look ----
!define MUI_ICON   "..\assets\mdu.ico"
!define MUI_UNICON "..\assets\mdu.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP   "wizard.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "wizard.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP "header.bmp"
!define MUI_HEADERIMAGE_RIGHT
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_NODESC

; ---- pages ----
!define MUI_WELCOMEPAGE_TITLE "Miller Driver Updater"
!define MUI_WELCOMEPAGE_TEXT "This installs Miller Driver Updater for your user account. (Earlier versions were called Miller Driver Update; this replaces them and keeps their reports.)$\r$\n$\r$\nIt checks this PC for driver updates (Windows Update), the newest NVIDIA GeForce driver (straight from nvidia.com, no account) and program updates (winget). You choose what to install; it makes a restore point and backs up each driver first, and it never restarts the PC on its own.$\r$\n$\r$\nNo administrator rights are needed to install it. The app asks for them when it opens, because installing drivers needs them."
!define MUI_FINISHPAGE_TITLE "Done"
!define MUI_FINISHPAGE_TEXT "Miller Driver Updater is in the Start menu.$\r$\n$\r$\nWhen it opens, say Yes to the $\"make changes to your device$\" prompt - installing drivers needs it. The first check takes under a minute on most PCs. Nothing changes until you click Install."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APPEXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Open Miller Driver Updater now"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Check for updates every Monday and notify me"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION WeeklyCheck
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_NOREBOOTSUPPORT

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

; /UPDATE: started by the app's own Update button (with /S). Wait for the app to close, and open it again at the end.
Var IsUpdate
Function .onInit
  StrCpy $IsUpdate 0
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/UPDATE" $R1
  ${IfNot} ${Errors}
    StrCpy $IsUpdate 1
  ${EndIf}
FunctionEnd

!define AVTEXT "This happens to new programs that are not code-signed. Malwarebytes, for example, reports it as $\"MachineLearning/Anomalous$\" - a guess about unknown programs, not a real virus.$\r$\n$\r$\nTo fix it:$\r$\n1. Open the antivirus' quarantine (Malwarebytes: Detection History), select MillerDriverUpdater.exe and restore it, choosing to always allow it if asked. Or add this folder to its allow list:$\r$\n     $INSTDIR$\r$\n2. Run this setup again."

; ---- install ----
Section "Miller Driver Updater" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"

  ; An exe already here (an update, or installing again) is kept as .old until the new one is known to run.
  ${If} ${FileExists} "$INSTDIR\${APPEXE}"
    ${If} $IsUpdate == 1
      ; The app closes itself right after starting this; wait (up to 30 s) until its exe is free.
      StrCpy $R2 0
      ${Do}
        ClearErrors
        FileOpen $R3 "$INSTDIR\${APPEXE}" a
        ${IfNot} ${Errors}
          FileClose $R3
          ${Break}
        ${EndIf}
        Sleep 500
        IntOp $R2 $R2 + 1
      ${LoopUntil} $R2 >= 60
    ${EndIf}
    Delete "$INSTDIR\${APPEXE}.old"
    Rename "$INSTDIR\${APPEXE}" "$INSTDIR\${APPEXE}.old"
  ${EndIf}
  File "..\dist\${APPEXE}"
  File "..\Read me first.txt"
  File "..\README.md"

  ; Some antivirus programs delete a new, unsigned program when it is written or first run (Malwarebytes reports it
  ; as "MachineLearning/Anomalous"). Run it once (--ping only starts and exits) and check
  ; before touching anything else, so an earlier version stays as it was and the person is told what to do.
  nsExec::Exec '"$INSTDIR\${APPEXE}" --ping'
  Pop $0
  Sleep 3000
  ${IfNot} ${FileExists} "$INSTDIR\${APPEXE}"
    ${If} ${FileExists} "$INSTDIR\${APPEXE}.old"
      Rename "$INSTDIR\${APPEXE}.old" "$INSTDIR\${APPEXE}"
    ${EndIf}
    ${If} $IsUpdate == 1
      ; An update is started from the app with /S, so this message is shown even then (no /SD), and the
      ; previous version is opened again.
      MessageBox MB_OK|MB_ICONEXCLAMATION "Your antivirus removed the new MillerDriverUpdater.exe as soon as it was installed, so the update was not applied. Your current version is unchanged.$\r$\n$\r$\n${AVTEXT}"
      ${If} ${FileExists} "$INSTDIR\${APPEXE}"
        Exec '"$INSTDIR\${APPEXE}"'
      ${EndIf}
    ${Else}
      MessageBox MB_OK|MB_ICONEXCLAMATION "Your antivirus removed MillerDriverUpdater.exe as soon as it was installed, so nothing was changed.$\r$\n$\r$\n${AVTEXT}" /SD IDOK
    ${EndIf}
    Abort "MillerDriverUpdater.exe was removed by the antivirus. Restore and allow it in the antivirus, then run setup again."
  ${EndIf}
  Delete "$INSTDIR\${APPEXE}.old"

  ; Replace "Miller Driver Update" (1.x PowerShell, 2.0 exe): its program files, shortcuts and uninstall entry go;
  ; its reports folder stays (the app still lists driver backups from there).
  Delete "${OLDDIR}\MillerDriverUpdate.exe"
  Delete "${OLDDIR}\Read me first.txt"
  Delete "${OLDDIR}\README.md"
  Delete "${OLDDIR}\Uninstall.exe"
  Delete "${OLDDIR}\Setup.cmd"
  Delete "${OLDDIR}\Uninstall.cmd"
  Delete "${OLDDIR}\Miller Driver Update.cmd"
  Delete "${OLDDIR}\mdu.cmd"
  Delete "${OLDDIR}\src\Mdu.psm1"
  Delete "${OLDDIR}\src\gui.ps1"
  Delete "${OLDDIR}\src\mdu.ps1"
  Delete "${OLDDIR}\src\setup.ps1"
  RMDir  "${OLDDIR}\src"
  Delete "${OLDDIR}\assets\mdu.ico"
  Delete "${OLDDIR}\assets\mdu-256.png"
  Delete "${OLDDIR}\assets\mdu-64.png"
  Delete "${OLDDIR}\assets\logo.svg"
  RMDir  "${OLDDIR}\assets"
  RMDir  "${OLDDIR}"
  Delete "$SMPROGRAMS\${OLDNAME}.lnk"
  Delete "$DESKTOP\${OLDNAME}.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${OLDID}"
  DeleteRegKey HKCU "Software\${OLDID}"

  CreateShortCut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0 SW_SHOWNORMAL "" "${LNKDESC}"

  ; A weekly check from any earlier version becomes "${TASKNAME}" running the new exe (--schedule removes the old task).
  nsExec::ExecToStack 'schtasks.exe /Query /TN "${TASKNAME}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    nsExec::ExecToStack 'schtasks.exe /Query /TN "${OLDTASK}"'
    Pop $0
    Pop $1
  ${EndIf}
  ${If} $0 == 0
    nsExec::ExecToStack '"$INSTDIR\${APPEXE}" --schedule'
    Pop $0
    Pop $1
  ${EndIf}

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr   HKCU "Software\${APPID}" "InstallDir" "$INSTDIR"
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayName"          "${APPNAME}"
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayVersion"       "${APPVER}"
  WriteRegStr   HKCU "${UNINSTKEY}" "Publisher"            "${APPNAME}"
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayIcon"          "$INSTDIR\${APPEXE},0"
  WriteRegStr   HKCU "${UNINSTKEY}" "InstallLocation"      "$INSTDIR"
  WriteRegStr   HKCU "${UNINSTKEY}" "UninstallString"      '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" "$0"

  ; Updated from inside the app: open the new version (it closed itself to make room).
  ${If} $IsUpdate == 1
    Exec '"$INSTDIR\${APPEXE}"'
  ${EndIf}
SectionEnd

Section "Desktop shortcut" SecDesktop
  CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0 SW_SHOWNORMAL "" "${LNKDESC}"
SectionEnd

Function WeeklyCheck
  ; Registers the Monday check as a scheduled task for this user. No window, no admin prompt.
  nsExec::ExecToStack '"$INSTDIR\${APPEXE}" --schedule'
  Pop $0
  Pop $1
FunctionEnd

; ---- uninstall ----
Section "Uninstall"
  nsExec::ExecToStack 'schtasks.exe /Delete /TN "${TASKNAME}" /F'
  Pop $0
  Pop $1
  nsExec::ExecToStack 'schtasks.exe /Delete /TN "${OLDTASK}" /F'
  Pop $0
  Pop $1
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$DESKTOP\${APPNAME}.lnk"

  Delete "$INSTDIR\${APPEXE}"
  Delete "$INSTDIR\${APPEXE}.old"
  Delete "$INSTDIR\Read me first.txt"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\Uninstall.exe"

  ; Reports, logs and driver backups: %LOCALAPPDATA%\Miller Driver Updater, plus anything the old name left behind
  ; (%LOCALAPPDATA%\Miller Driver Update from 2.0, Programs\Miller Driver Update\reports from 1.x).
  StrCpy $1 "$LOCALAPPDATA\${APPNAME}"
  StrCpy $2 "$LOCALAPPDATA\${OLDNAME}"
  ${If} ${FileExists} "$1\reports\*.*"
  ${OrIf} ${FileExists} "$2\reports\*.*"
  ${OrIf} ${FileExists} "${OLDDIR}\reports\*.*"
    MessageBox MB_YESNO|MB_ICONQUESTION "Also delete the reports and driver backups?$\r$\n$\r$\n$1" /SD IDNO IDNO keepdata
    RMDir /r "$1"
    RMDir /r "$2"
    RMDir /r "${OLDDIR}\reports"
    RMDir "${OLDDIR}"
  ${EndIf}
  keepdata:
  RMDir "$INSTDIR"

  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\${APPID}"
SectionEnd
