# Architecture

## Processes and entry points

`ImgConv.exe` is one binary with several modes, picked by command-line arguments (`Program.Main`):

| Arguments | Mode |
|---|---|
| *(none)* | Tray icon + main window. Single instance: a second launch signals the first one to show its window. |
| `/tray` | Tray icon only (used by autostart). |
| `/to <fmt> <files...>` | Convert and exit. Used by both Explorer menus. |
| `/setup <menu 0/1> <autostart 0/1>` | Called by the installer to register the Explorer menu and autostart with the right paths. |
| `/uninstall [/quiet]` | Removes everything, then deletes its own folder after exiting. |
| `/mkico`, `/mkpng` | Build-time helpers that draw the icon and the package logo. |

## Conversion pipeline (`Conv`)

1. **Load**: read the whole file into memory (the source is never locked or modified).
   - JPEG XL → `ImgConvCodecs.dll` if present.
   - Otherwise GDI+; if GDI+ can't decode it (WebP, HEIC, AVIF), WPF/WIC, which uses the codecs installed in Windows.
   - EXIF orientation is applied to the pixels.
2. **Resize** if a max long side is set (high-quality bicubic).
3. **Flatten** onto white for JPG (no transparency).
4. **Encode** with the size limit (`Conv.Encode`):
   - Lossy (JPG): binary search for the highest quality in `[30, q]` that fits.
   - If even quality 30 is too big, or the format is lossless: scale by `sqrt(limit / size) × 0.95` and retry.
   - JPG uses jpegli when the codecs DLL is available and enabled, else the GDI+ encoder.
5. **Write** next to the source (`name.jpg`, `name (1).jpg`, ...) or to a given folder (clipboard pastes).

Metadata is never copied: output is encoded from pixels only, so GPS location and camera data are dropped.

## Explorer integration

- **Classic menu** (Windows 10, and "Show more options" on Windows 11): registry verbs under
  `HKCU\Software\Classes\SystemFileAssociations\image\shell\ImgConv`, plus `...\.jxl\shell\ImgConv`
  because `.jxl` usually isn't registered as an image type. Each verb runs `ImgConv.exe /to <fmt> "%1"`.
- **Windows 11 menu**: `menu/ImgConvMenu.dll` implements `IExplorerCommand` ("Convert to" with sub-commands).
  Windows only shows such handlers for packaged apps, so `menu/AppxManifest.xml` describes a *sparse* MSIX
  package (identity only, files stay in the install folder), signed with a self-signed `CN=ImgConv` certificate.
  The DLL launches `ImgConv.exe /to <fmt> <all selected files>` and hides itself when `Menu=0`.

### Multi-select batching

The classic menu starts one process per selected file. The first process takes the named mutex
`ImgConv.Batch.*` and listens on a named pipe for 0.5 s; the others send their file paths to it and exit.
So 30 selected files produce one size/quality dialog and one "Done" popup.

## Settings (`HKCU\Software\ImgConv`)

| Value | Meaning | Default |
|---|---|---|
| `Format` | Last output format in the window | `jpg` |
| `Quality` | Max JPG quality, 1–100 | 100 |
| `TargetKB` | Size limit, 0 = none | 490 |
| `MaxSide` | Max long side in px, 0 = none | 0 |
| `Ask` | Show the size/quality dialog for Explorer conversions | 1 |
| `Notify` | Show the "Done" popup after Explorer conversions | 1 |
| `Jpegli` | Use jpegli for JPG when the codecs DLL is present | 1 |
| `Menu` | Explorer menus on/off (read by the Windows 11 DLL too) | 1 |

Autostart: `HKCU\...\Run\ImgConv`. Uninstall entry: `HKCU\...\Uninstall\ImgConv`.

## Optional codecs (`codecs/ImgConvCodecs.dll`)

A small C API over static libjxl v0.11.1: `ic_jpegli_encode`, `ic_jxl_decode`, `ic_free`.
Loaded by P/Invoke only when converting, so it costs nothing while the app idles in the tray.
If it's missing or fails to load, the app falls back to the Windows codecs.

## Installer (`Setup.cs`)

- Payload (exe, DLLs, MSIX, certificate, license notices) is embedded as gzip-compressed resources.
- Installs per user to `%LOCALAPPDATA%\Programs\ImgConv`, no admin rights.
- Windows 11 menu: if the certificate isn't trusted yet, one UAC prompt runs
  `certutil -addstore TrustedPeople ImgConv.cer`, then `Add-AppxPackage -ExternalLocation` registers the package.
  Declining just skips the new menu.
- `/S` installs silently with defaults.
