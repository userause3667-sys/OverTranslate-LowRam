; OverTranslate Low-RAM Edition - installer (NSIS 3, Unicode)
; Build:  makensis /DVERSION=2.11.0 /DSOURCEDIR=<publish dir> /DICON=<app.ico> /DOUTFILE=<Setup.exe> OverTranslate-LowRam.nsi
; Per-user install: KHONG can quyen Administrator, KHONG sua Registry he thong, KHONG sua Defender/pagefile.
; Chi ghi: thu muc cai dat, shortcut, va 1 khoa Uninstall duoi HKCU (de co trong "Apps & features").
; Du lieu nguoi dung o %AppData%\OverTranslate duoc GIU NGUYEN khi go cai dat.
Unicode true
!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef SOURCEDIR
  !error "Thieu /DSOURCEDIR=<thu muc publish>"
!endif
!ifndef OUTFILE
  !define OUTFILE "OverTranslate-LowRam-Setup.exe"
!endif

!define APPNAME "OverTranslate Low-RAM"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\OverTranslateLowRam"

Name "${APPNAME} ${VERSION}"
OutFile "${OUTFILE}"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\OverTranslate-LowRam"
InstallDirRegKey HKCU "Software\OverTranslateLowRam" "InstallDir"
SetCompressor /SOLID lzma
SetCompressorDictSize 32
BrandingText "OverTranslate Low-RAM ${VERSION} (MIT)"
!ifdef ICON
  !define MUI_ICON "${ICON}"
  !define MUI_UNICON "${ICON}"
!endif

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "MIT License - see LICENSE"
VIAddVersionKey /LANG=1033 "FileDescription" "${APPNAME} installer"

!include "MUI2.nsh"
!include "FileFunc.nsh"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\OverTranslate.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Mo OverTranslate / Launch OverTranslate"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${SOURCEDIR}\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Vietnamese"
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ; Neu ung dung dang chay thi file bi khoa -> bao nguoi dung thoat truoc.
  nsExec::ExecToStack 'cmd /c tasklist /FI "IMAGENAME eq OverTranslate.exe" | find /I "OverTranslate.exe"'
  Pop $0
  Pop $1
  ${If} $0 == 0
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "OverTranslate dang chay. Hay thoat ung dung (bieu tuong khay he thong) roi bam OK de tiep tuc.$\r$\n$\r$\nOverTranslate is running. Please exit it from the tray, then click OK." IDOK +2
    Abort
  ${EndIf}
FunctionEnd

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "${SOURCEDIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\OverTranslate Low-RAM.lnk" "$INSTDIR\OverTranslate.exe"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Huong dan (HUONG_DAN).lnk" "$INSTDIR\HUONG_DAN.md"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Go cai dat.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\OverTranslate Low-RAM.lnk" "$INSTDIR\OverTranslate.exe"

  WriteRegStr HKCU "Software\OverTranslateLowRam" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "OverTranslate (MIT)"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\OverTranslate.exe"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  ; Chi xoa thu muc cai dat + shortcut. KHONG dong toi %AppData%\OverTranslate (cai dat, cache, tu dien cua ban).
  Delete "$DESKTOP\OverTranslate Low-RAM.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\OverTranslateLowRam"
SectionEnd
