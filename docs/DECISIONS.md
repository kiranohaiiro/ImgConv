# Decision history

Why things are the way they are. Newest last. Before changing one of these, read the reasoning and
add a new entry instead of silently reversing an old one.

---

### D1. .NET Framework 4 + WinForms, single C# file (2026-10-27)

**Decision:** Write the app in C# 5 on .NET Framework 4 with WinForms, compiled by the `csc.exe` that ships with Windows.
**Why:** Every Windows 10/11 PC already has the runtime, so the app is ~80 KB with no download. Anyone can build it without installing Visual Studio.
**Rejected:** .NET 8 (runtime download or 60+ MB self-contained), WPF/WinUI (heavier, slower start), pure C++ (much more code for the UI).
**Trade-off:** C# 5 syntax only.

### D2. Settings in the registry (2026-10-27)

**Decision:** `HKCU\Software\ImgConv`.
**Why:** No config file to locate, works the same for the tray process and the short-lived `/to` processes, and the Windows 11 menu DLL can read the same values.

### D3. A "target file size" search instead of a quality slider (2026-10-27)

**Decision:** Binary-search JPG quality between 30 and the chosen maximum; below 30, downscale instead.
**Why:** The real-world need is "under 500 KB", not "quality 73". Below quality ~30 JPGs get blocky; a slightly smaller image looks better than a mushy one.

### D4. Custom installer, not Inno Setup / WiX / MSI (2026-10-27)

**Decision:** A small C# installer (`Setup.cs`) with the payload as embedded resources.
**Why:** No extra tools to build it, per-user install without admin, full control over the Explorer and Windows 11 menu steps. Uninstall entry and silent mode (`/S`, `/uninstall /quiet`) are provided by hand.

### D5. English UI (2026-10-27)

**Decision:** The interface is English.
**Why:** The audience found the app through English-speaking communities.

### D6. One dialog for a multi-file selection (2026-10-27)

**Decision:** Processes started by the classic menu elect a leader via a named mutex and send their paths over a named pipe.
**Why:** Explorer starts one process per file; without batching, selecting 20 photos opened 20 dialogs.

### D7. Windows 11 menu via a signed sparse MSIX (2026-10-27)

**Decision:** A C++ `IExplorerCommand` DLL registered by a sparse package signed with a self-signed certificate; the installer trusts the certificate after one UAC prompt.
**Why:** Windows 11 shows only packaged handlers in its new menu. A paid code-signing certificate would avoid the UAC prompt but costs money every year.
**Trade-offs:** One optional UAC prompt; x64 only; if the certificate is regenerated, users confirm it again. Declining still leaves the classic menu.

### D8. jpegli for JPG via an optional DLL (2026-10-29)

**Decision:** Build libjxl statically into `ImgConvCodecs.dll` and use jpegli for JPG encoding, with a fallback to GDI+.
**Why:** Requested on Reddit. Measured 7–12% smaller files at the same quality and +0.7…1.3 dB PSNR under a 30 KB limit (see [research/jpegli.md](research/jpegli.md)). The DLL is 1.4 MB but loads only while converting.

### D9. Read JPEG XL, don't write it (2026-10-29)

**Decision:** Ship the JXL decoder, not the encoder.
**Why:** Reading needed no Store extension and fits in the same DLL. The encoder was built and worked (q90 photo: 24.6 KB vs 31 KB jpegli), but it grew the DLL from 1.4 to 6.5 MB and the installer from 800 KB to 3.4 MB. Restricting Highway SIMD targets didn't help: MSVC builds only one target anyway. Staying small won.

### D10. gzip-compressed installer payload (2026-10-29)

**Decision:** `build.ps1` gzips every embedded file; `Setup.cs` unpacks with `GZipStream`.
**Why:** Installer 1.7 MB → 800 KB.

### D11. No network, ever (2026-10-27)

**Decision:** No update checks, telemetry or online features.
**Why:** "Your photos never leave your PC" is the main reason people use this instead of a website. Updates are announced on GitHub Releases.

### D12. Metadata is always removed (2026-10-31)

**Decision:** Outputs never contain EXIF/GPS. No "keep metadata" option for now.
**Why:** It comes for free (encoding from pixels) and protects people uploading phone photos to marketplaces and forms. Verified: GPS, camera make and date tags are gone from JPG and PNG outputs.

### D13. v1.3 conveniences, not features (2026-10-31)

**Decision:** Add size presets, a "Done" popup with "Show in folder" / "Copy file", and Ctrl+V for screenshots. Decline filters, editing and watermarks.
**Why:** Reddit feedback compared the app with XnConvert, FileConverter and PowerToys. Competing on feature count is a losing game; making the one job faster is not.
