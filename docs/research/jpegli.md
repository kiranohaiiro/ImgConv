# jpegli vs the Windows JPEG encoder

Measured 2026-10-29 with ImgConv's own pipeline (`ImgConv.exe /to jpg`), libjxl v0.11.1.
Photos: the 500×500 sRGB images from `libjxl/testdata/external/wesaturate/500px`.
PSNR is computed against the source PNG; higher is better.

## Same quality setting (90)

| Photo | Windows encoder | jpegli |
|---|---|---|
| cvo9xd | 50.6 KB, 35.70 dB | **47.3 KB, 36.10 dB** |
| tmshre | 35.2 KB, 41.30 dB | **31.0 KB, 42.16 dB** |
| u76c0g | 49.5 KB, 38.60 dB | **45.2 KB, 39.47 dB** |

jpegli: 7–12% smaller *and* more accurate.

## Same size limit (30 KB)

| Photo | Windows encoder | jpegli |
|---|---|---|
| cvo9xd | 29.4 KB, 33.40 dB | 29.5 KB, **34.08 dB** |
| tmshre | 30.0 KB, 40.57 dB | 29.2 KB, **41.86 dB** |
| u76c0g | 29.9 KB, 35.73 dB | 28.7 KB, **36.64 dB** |

+0.7…1.3 dB at the same size. Visually, the Windows encoder breaks smooth backgrounds into 8×8 blocks
(see [../jpegli-vs-windows.png](../jpegli-vs-windows.png)); jpegli keeps them smooth.

## JPEG XL encoder (not shipped)

Same pipeline, `tmshre` photo: quality 90 → 24.6 KB; quality 100 → lossless 215 KB; 30 KB limit respected; alpha preserved.
Not shipped because of size (see decision D9).
