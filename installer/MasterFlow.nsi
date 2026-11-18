; MasterFlow per-user installer (roadmap G1). Build: makensis -DVERSION=x.y.z -DSOURCE=<dir with MasterFlow.exe> -DOUTFILE=<setup.exe> installer/MasterFlow.nsi
; No administrator rights: installs to %LOCALAPPDATA%\Programs\MasterFlow and writes only HKCU.
; Uninstall removes the program, shortcut, file association and uninstall entry. It keeps the user's
; projects and %APPDATA%\MasterFlow (recent files, templates, recovery copy, log).

Unicode true
!include "MUI2.nsh"

!ifndef VERSION
  !error "Pass -DVERSION=x.y.z"
!endif
!ifndef SOURCE
  !error "Pass -DSOURCE=<folder containing MasterFlow.exe>"
!endif
!ifndef OUTFILE
  !define OUTFILE "MasterFlow-${VERSION}-setup.exe"
!endif

!define APP "MasterFlow"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MasterFlow"
!define PROGID "MasterFlow.Project"

Name "${APP} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP}"
InstallDirRegKey HKCU "${UNINSTKEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ShowInstDetails nevershow
ShowUninstDetails nevershow

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APP}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${APP} setup"
VIAddVersionKey "LegalCopyright" ""

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\MasterFlow.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Start MasterFlow"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "MasterFlow" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  File "${SOURCE}\MasterFlow.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateShortCut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\MasterFlow.exe"

  ; .mfproj opens in MasterFlow (per user).
  WriteRegStr HKCU "Software\Classes\.mfproj" "" "${PROGID}"
  WriteRegStr HKCU "Software\Classes\${PROGID}" "" "MasterFlow project"
  WriteRegStr HKCU "Software\Classes\${PROGID}\DefaultIcon" "" '"$INSTDIR\MasterFlow.exe",0'
  WriteRegStr HKCU "Software\Classes\${PROGID}\shell\open\command" "" '"$INSTDIR\MasterFlow.exe" "%1"'
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'

  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "${APP}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" '"$INSTDIR\MasterFlow.exe"'
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" 102400
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\MasterFlow.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APP}.lnk"

  ; Remove the association only if it still points at MasterFlow.
  ReadRegStr $0 HKCU "Software\Classes\.mfproj" ""
  StrCmp $0 "${PROGID}" 0 +2
    DeleteRegKey HKCU "Software\Classes\.mfproj"
  DeleteRegKey HKCU "Software\Classes\${PROGID}"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'

  DeleteRegKey HKCU "${UNINSTKEY}"
SectionEnd
