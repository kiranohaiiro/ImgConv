# Changelog

## 1.3.1 — 2026-10-31

- Silent install (`/S`, used by winget) never shows a UAC prompt. The Windows 11 menu is set up silently only if its certificate is already trusted; otherwise the classic menu is used.
- Builds now run on GitHub Actions with a smoke test (silent install, convert under 490 KB, uninstall).
- [CODE_SIGNING.md](CODE_SIGNING.md): code signing policy.

## 1.3 — 2026-10-31

- **Size presets**: Upload forms 490 KB, Small 200 KB, Email 2 MB, Discord 10 MB, Marketplace (eBay) 1600 px / 1 MB. In the window and in the right-click dialog.
- **"Done" popup** after converting from Explorer, with *Show in folder* and *Copy file* (paste it straight into a chat or an upload form). Can be turned off in settings.
- **Ctrl+V in the window**: paste a screenshot → it's converted to Pictures\ImgConv and the result is copied back as a file. Copied files are converted like dropped ones.
- The right-click dialog now also has **max long side**.
- Location and camera data (EXIF/GPS) are confirmed removed from every output, now documented.
- Docs for contributors and AI assistants: `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`.

## 1.2 — 2026-10-29

- **jpegli** JPEG encoder: 7–12% smaller files at the same quality, cleaner under tight limits.
- **Opens JPEG XL** without any Store extension.
- Installer compressed to ~800 KB.

## 1.1 — 2026-10-27

- **"Convert to" in the new Windows 11 context menu** (signed sparse MSIX, one optional UAC prompt).
- Size and quality dialog for right-click conversions (defaults 490 KB, quality 100); one dialog for a multi-file selection.
- Settings moved into the main window; Explorer menu on by default; English UI.

## 1.0 — 2026-10-27

- First release: tray app, right-click "Convert to", target file size, installer.
