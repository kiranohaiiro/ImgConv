# Code signing policy

**Status:** an application for free open-source code signing from [SignPath Foundation](https://signpath.org) is in progress.
Until it's approved, releases are unsigned (Windows SmartScreen shows a warning once) and the Windows 11
menu package uses a self-signed certificate. This page describes how signing will work.

Once approved: *Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).*

## What gets signed

Only files built by the public GitHub Actions workflow ([`.github/workflows/build.yml`](.github/workflows/build.yml))
from the source code in this repository:

- `ImgConv-Setup.exe` (installer) and the files it installs: `ImgConv.exe`, `ImgConvMenu.dll`, `ImgConvCodecs.dll`, `ImgConv.msix`.

Nothing built on a personal computer is signed. Third-party code (libjxl and its dependencies, see
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)) is built from source in the same workflow.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [@kiranohaiiro](https://github.com/kiranohaiiro) |
| Approvers | [@kiranohaiiro](https://github.com/kiranohaiiro) |

Every signing request for a release is approved manually by an approver.

## Privacy policy

This program will not transfer any information to other networked systems.
It never connects to the internet: no telemetry, no update checks, no online services.
Images are processed locally, and the converted files have location and camera metadata (EXIF/GPS) removed.
