# Building the RetroJukebox Installer

The installer is built using **NSIS** (Nullsoft Scriptable Install System) — a
free, industry-standard Windows installer tool used by many well-known apps.

It requires no Visual Studio integration and produces a standard
`RetroJukebox-1.0.0-Setup.exe` that users can double-click to install.

---

## Prerequisites (one-time setup)

### 1. Install NSIS
Download and install from **https://nsis.sourceforge.io/Download**
- Choose the latest stable release (3.x)
- Use the default install location (`C:\Program Files (x86)\NSIS`)

### 2. Ensure .NET 8 SDK is installed
Download from **https://dotnet.microsoft.com/download/dotnet/8.0**

---

## Building the installer

Double-click **`build-installer.bat`** in the solution root folder.

It will automatically:
1. Publish the app as a self-contained Windows x64 build  
   (bundles the .NET 8 runtime — end users don't need to install .NET)
2. Run NSIS to compile the installer script
3. Output `RetroJukebox-1.0.0-Setup.exe` in the solution root

---

## What the installer does

- Installs to `C:\Program Files\RetroJukebox`
- Creates a **Desktop shortcut**
- Creates a **Start Menu** folder with launch and uninstall shortcuts
- Adds an entry to **Add/Remove Programs** (Settings → Apps)
- Includes a proper **Uninstaller** that cleanly removes all files

---

## Customising

To change the version number, update:
- `VIProductVersion` and `VIAddVersionKey "ProductVersion"` in `RetroJukebox.nsi`
- The `OutFile` name in `RetroJukebox.nsi`
- `<Version>` in `RetroJukebox\RetroJukebox.csproj`
- The output filename in `build-installer.bat`
