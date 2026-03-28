; ============================================================
;  RetroJukebox Installer — NSIS Script
;  Build with: makensis RetroJukebox.nsi
;  (See README-Installer.md for full instructions)
; ============================================================

!include "MUI2.nsh"
!include "FileFunc.nsh"

; ── Product metadata ──────────────────────────────────────────────────────
Name              "RetroJukebox"
OutFile           "..\RetroJukebox-1.0.0-Setup.exe"
InstallDir        "$PROGRAMFILES64\RetroJukebox"
InstallDirRegKey  HKLM "Software\RetroJukebox" "InstallDir"
RequestExecutionLevel admin
Unicode True

VIProductVersion  "1.0.0.0"
VIAddVersionKey   "ProductName"      "RetroJukebox"
VIAddVersionKey   "CompanyName"      "RetroJukebox"
VIAddVersionKey   "FileDescription"  "RetroJukebox Music Player Installer"
VIAddVersionKey   "FileVersion"      "1.0.0.0"
VIAddVersionKey   "ProductVersion"   "1.0.0"
VIAddVersionKey   "LegalCopyright"   "Copyright 2025"

; ── MUI settings ─────────────────────────────────────────────────────────
!define MUI_ABORTWARNING
!define MUI_ICON   "..\RetroJukebox\Resources\RetroJukebox.ico"
!define MUI_UNICON "..\RetroJukebox\Resources\RetroJukebox.ico"

; Header/welcome image branding colours (orange on dark)
!define MUI_HEADERIMAGE
!define MUI_BGCOLOR     "0D0E12"
!define MUI_TEXTCOLOR   "E8EAF0"

; ── Installer pages ───────────────────────────────────────────────────────
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE     "License.rtf"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; ── Uninstaller pages ────────────────────────────────────────────────────
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ── Install section ───────────────────────────────────────────────────────
Section "RetroJukebox" SecMain

  SectionIn RO  ; required — cannot be deselected

  SetOutPath "$INSTDIR"

  ; Copy all published files from the Publish folder
  ; Run build-installer.bat first to populate this folder
  File /r "..\RetroJukebox\bin\Publish\*.*"

  ; Write install location to registry
  WriteRegStr HKLM "Software\RetroJukebox" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\RetroJukebox" "Version"    "1.0.0"

  ; Add/Remove Programs entry
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RetroJukebox" \
                "DisplayName"          "RetroJukebox"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RetroJukebox" \
                "UninstallString"      '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RetroJukebox" \
                "DisplayIcon"          "$INSTDIR\RetroJukebox.exe"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RetroJukebox" \
                "Publisher"            "RetroJukebox"
  WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RetroJukebox" \
                "DisplayVersion"       "1.0.0"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RetroJukebox" \
                "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RetroJukebox" \
                "NoRepair" 1

  ; Estimate installed size for Add/Remove Programs
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RetroJukebox" \
                "EstimatedSize" "$0"

  ; Desktop shortcut
  CreateShortcut "$DESKTOP\RetroJukebox.lnk" \
                 "$INSTDIR\RetroJukebox.exe" "" \
                 "$INSTDIR\RetroJukebox.exe" 0

  ; Start Menu folder + shortcuts
  CreateDirectory "$SMPROGRAMS\RetroJukebox"
  CreateShortcut  "$SMPROGRAMS\RetroJukebox\RetroJukebox.lnk" \
                  "$INSTDIR\RetroJukebox.exe" "" \
                  "$INSTDIR\RetroJukebox.exe" 0
  CreateShortcut  "$SMPROGRAMS\RetroJukebox\Uninstall RetroJukebox.lnk" \
                  "$INSTDIR\Uninstall.exe"

  ; File associations — register RetroJukebox as a handler for audio files
  ; Each extension points to the RetroJukebox.AudioFile ProgID
  WriteRegStr HKCR "RetroJukebox.AudioFile"                          "" "Audio File"
  WriteRegStr HKCR "RetroJukebox.AudioFile\DefaultIcon"              "" "$INSTDIR\RetroJukebox.exe,0"
  WriteRegStr HKCR "RetroJukebox.AudioFile\shell\open\command"       "" '"$INSTDIR\RetroJukebox.exe" "%1"'

  WriteRegStr HKCR ".mp3\OpenWithProgids"  "RetroJukebox.AudioFile" ""
  WriteRegStr HKCR ".wav\OpenWithProgids"  "RetroJukebox.AudioFile" ""
  WriteRegStr HKCR ".flac\OpenWithProgids" "RetroJukebox.AudioFile" ""
  WriteRegStr HKCR ".ogg\OpenWithProgids"  "RetroJukebox.AudioFile" ""
  WriteRegStr HKCR ".aac\OpenWithProgids"  "RetroJukebox.AudioFile" ""
  WriteRegStr HKCR ".m4a\OpenWithProgids"  "RetroJukebox.AudioFile" ""
  WriteRegStr HKCR ".wv\OpenWithProgids"   "RetroJukebox.AudioFile" ""
  WriteRegStr HKCR ".ape\OpenWithProgids"  "RetroJukebox.AudioFile" ""

  ; Notify Windows that file associations changed
  System::Call 'shell32.dll::SHChangeNotify(i, i, i, i) v (0x08000000, 0, 0, 0)'

  ; Write uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"

SectionEnd

; ── Uninstall section ─────────────────────────────────────────────────────
Section "Uninstall"

  ; Remove all installed files
  RMDir /r "$INSTDIR"

  ; Remove shortcuts
  Delete "$DESKTOP\RetroJukebox.lnk"
  RMDir /r "$SMPROGRAMS\RetroJukebox"

  ; Remove registry entries
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RetroJukebox"
  DeleteRegKey HKLM "Software\RetroJukebox"

  ; Remove file association ProgID
  DeleteRegKey HKCR "RetroJukebox.AudioFile"

  ; Remove from OpenWithProgids for each extension
  DeleteRegValue HKCR ".mp3\OpenWithProgids"  "RetroJukebox.AudioFile"
  DeleteRegValue HKCR ".wav\OpenWithProgids"  "RetroJukebox.AudioFile"
  DeleteRegValue HKCR ".flac\OpenWithProgids" "RetroJukebox.AudioFile"
  DeleteRegValue HKCR ".ogg\OpenWithProgids"  "RetroJukebox.AudioFile"
  DeleteRegValue HKCR ".aac\OpenWithProgids"  "RetroJukebox.AudioFile"
  DeleteRegValue HKCR ".m4a\OpenWithProgids"  "RetroJukebox.AudioFile"
  DeleteRegValue HKCR ".wv\OpenWithProgids"   "RetroJukebox.AudioFile"
  DeleteRegValue HKCR ".ape\OpenWithProgids"  "RetroJukebox.AudioFile"

  System::Call 'shell32.dll::SHChangeNotify(i, i, i, i) v (0x08000000, 0, 0, 0)'

SectionEnd
