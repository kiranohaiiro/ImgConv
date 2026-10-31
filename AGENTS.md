# AGENTS.md

Instructions for AI coding assistants (Claude, Copilot, Cursor, ...) and for humans working on ImgConv.
Read this first, then [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/DECISIONS.md](docs/DECISIONS.md).

## What this project is

A tiny Windows image converter with one job: turn an image into JPG/PNG/BMP/GIF/TIFF and make it fit
a file-size limit, from the Explorer right-click menu or a small tray window. Everything else is secondary.

## Principles (check every change against these)

1. **One job.** Convert and fit a size limit. No filters, editing, watermarks or galleries.
   If a feature doesn't make "photo → file the upload form accepts" shorter, it doesn't belong here.
2. **Small.** The installer is about 800 KB. Treat ~1 MB as a budget. A feature that pushes the installer
   noticeably past it needs a very good reason (see the JPEG XL encoder decision).
3. **Idle cost is zero.** About 4 MB of RAM in the tray, no timers, no polling. Heavy code (codecs) loads only while converting.
4. **Offline.** The app never touches the network: no telemetry, no update checks. Don't add any.
5. **Classic UI.** Real WinForms buttons and dropdowns. Users praised this; don't "modernize" it.
6. **Fallbacks, not hard dependencies.** Optional parts (codecs DLL, Windows 11 menu) must fail quietly
   and leave the basic app working.

## Repository map

| Path | What |
|---|---|
| `ImgConv.cs` | The whole app: tray, window, dialogs, conversion, Explorer menu registration, uninstall |
| `Setup.cs` | Installer (compiled together with `ImgConv.cs`, entry point `SetupApp`) |
| `menu/` | Windows 11 context menu: C++ `IExplorerCommand` DLL + sparse MSIX manifest |
| `codecs/` | Optional `ImgConvCodecs.dll`: jpegli encoder + JPEG XL decoder over libjxl |
| `build.ps1` | Builds everything into `ImgConv-Setup.exe` |
| `build-codecs.ps1` | One-time build of libjxl into `%LOCALAPPDATA%\ImgConvBuild` |
| `docs/` | Architecture, decisions, research, screenshots |

## Building

```
powershell -ExecutionPolicy Bypass -File build-codecs.ps1   # once; optional (jpegli / JPEG XL)
powershell -ExecutionPolicy Bypass -File build.ps1
```

- App and installer: the C# compiler that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).
- Windows 11 menu and codecs: Visual Studio Build Tools (C++) + Windows SDK. Missing → those parts are skipped.
- Output: `ImgConv-Setup.exe` in the repo root, intermediates in `dist/` (git-ignored).

## Gotchas that already cost time

- **C# 5 only.** `csc.exe` from .NET Framework 4 understands C# 5: no `$"..."` interpolation, no `?.`,
  no `=>` members, no `out var`, no `nameof`. Use `string.Format`/concatenation and full property syntax.
- **Source encoding.** `csc` is called with `/codepage:65001`, `cl` with `/utf-8`. Keep both.
- **PowerShell scripts must be pure ASCII.** Windows PowerShell 5.1 reads BOM-less files as ANSI;
  a single em dash breaks parsing.
- **`vcvars64.bat` is unreliable on some machines.** The scripts set `INCLUDE`/`LIB`/`PATH` and tool paths explicitly.
- **Long paths.** libjxl sources don't fit under deep folders, so they live in `%LOCALAPPDATA%\ImgConvBuild`.
- **Explorer keeps `ImgConvMenu.dll` loaded.** You can't overwrite or delete it, but you can rename it.
  Installer and uninstaller move it away first (`Win11Menu.MoveAwayDll`). Restart Explorer to see menu DLL changes.
- **Registry is the settings store** (`HKCU\Software\ImgConv`). Tests that change settings must export and restore it.

## Testing

There is no unit-test project; changes are verified by running the real exe:

- `ImgConv.exe /to jpg "a.png" "b.png"` converts with the saved settings (set `Ask=0` in the registry to skip the dialog,
  `Notify=0` to skip the popup).
- Sample photos and `.jxl` files: `%LOCALAPPDATA%\ImgConvBuild\libjxl\testdata` (after `build-codecs.ps1`).
- Windows can render a form off-screen with `Control.DrawToBitmap`; that's how `docs/*.png` screenshots are made.
- Installer: `ImgConv-Setup.exe /S` (silent), uninstall: `ImgConv.exe /uninstall /quiet`.

## Don'ts

- Don't add network access, telemetry or auto-update.
- Don't add image editing features.
- Don't switch to .NET 6+/WPF/WinUI: that adds a runtime download or tens of megabytes.
- Don't commit `dist/`, `*.exe`, certificates or anything from `%LOCALAPPDATA%\ImgConvBuild`.
