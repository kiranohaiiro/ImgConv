# ImgConv

**A tiny Windows tray app that does one thing: converts images to another format and squeezes them to the file size you need.**

PNG → JPG, WebP/HEIC → JPG, "make this photo under 500 KB" — drop the file in, done. No accounts, no ads, no uploading your photos to a random website, no 200 MB Electron app.

![Main window](docs/window.png)

## Why

Every form on the internet wants something different: "JPG only", "max 2 MB", "max 300 KB". Every time, the same routine — google an online converter, upload a personal photo to who-knows-where, wait, download, find out it's still too big, repeat. ImgConv lives in the tray and does it locally in about a second.

## Features

- **Convert** to JPG, PNG, BMP, GIF, TIFF.
- **Reads** JPG, PNG, BMP, GIF, TIFF, ICO, plus WebP / HEIC / AVIF through the codecs Windows already has.
- **Target file size** — set "no larger than N KB" and ImgConv finds the best JPG quality that fits. If even quality 30 is too big, it downscales the image instead of turning it into mush.
- **Max long side** in pixels (e.g. 1920).
- **Right-click menu in Explorer** (on by default): *Convert to → JPG / PNG / …* — a small dialog asks the max size and quality (defaults: **490 KB, quality 100**). Select 20 photos → still one dialog. You can turn the dialog off to convert instantly with saved settings.

  ![Size and quality dialog](docs/ask.png)
- Drag & drop files or whole folders. Respects EXIF rotation. Transparent PNGs get a white background in JPG.
- Originals are never touched: the result is saved next to the source (`photo.jpg`, `photo (1).jpg`, …).

## Lightweight on purpose

| | |
|---|---|
| Installer | ~150 KB |
| Program | ~70 KB, single `.exe` |
| RAM while idle in tray | ~4 MB working set |
| CPU while idle | 0 — no timers, no polling, no update checks |
| Dependencies | none — uses .NET Framework 4, built into every Windows 10/11 |
| Admin rights | not needed |
| Network | never touches it |

## Install

1. Download **`ImgConv-Setup.exe`** from [Releases](../../releases).
2. Run it, pick the options, click **Install**.

![Installer](docs/installer.png)

It installs to `%LOCALAPPDATA%\Programs\ImgConv` and shows up in *Settings → Apps*, where you can uninstall it (removes everything: files, shortcuts, menu entry, autostart, settings).

> Windows SmartScreen may warn about an unknown publisher because the exe isn't code-signed. Click *More info → Run anyway*, or build it yourself from source (below).

Silent install: `ImgConv-Setup.exe /S`

## Usage

- **Click the tray icon** → window. Drop photos in, or click to pick files. Settings (Explorer menu, the size/quality dialog, autostart) are at the bottom of the window.
- **Right-click photos in Explorer** → *Convert to* → format. On Windows 11 it's under *Show more options* (or Shift+F10).

Command line: `ImgConv.exe /to jpg "C:\path\photo.png"` — converts using saved settings and exits.

## Build from source

No Visual Studio needed — the C# compiler ships with Windows:

```bat
build.cmd
```

Produces `ImgConv-Setup.exe` (with the program embedded) and `dist\ImgConv.exe`.

Source: [`ImgConv.cs`](ImgConv.cs) (the app) and [`Setup.cs`](Setup.cs) (the installer). ~800 lines in total.

## Limitations

- Can't *save* WebP/HEIC/AVIF — Windows has no built-in encoder for them (reading works).
- Size limit for PNG/GIF/BMP/TIFF works only by downscaling, since they're lossless.
- Animated GIFs: only the first frame is kept.

## License

MIT
