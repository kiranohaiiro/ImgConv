# One-time step: builds the libjxl static libraries (jpegli encoder + JPEG XL decoder)
# that build.ps1 links into ImgConvCodecs.dll.
#   powershell -ExecutionPolicy Bypass -File build-codecs.ps1
#
# Downloads libjxl v0.11.1 from https://github.com/libjxl/libjxl (BSD-3-Clause) into
# %LOCALAPPDATA%\ImgConvBuild (a short path: libjxl's sources don't fit in long ones)
# and builds it with Visual Studio Build Tools (C++), CMake and Ninja.
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'ImgConvBuild'
$src = Join-Path $root 'libjxl'
$out = Join-Path $root 'out'
New-Item -ItemType Directory $root -Force | Out-Null

if (-not (Test-Path "$src\CMakeLists.txt")) {
    git -c advice.detachedHead=false -c core.longpaths=true clone --depth 1 --branch v0.11.1 `
        --recursive --shallow-submodules https://github.com/libjxl/libjxl $src
    if ($LASTEXITCODE) { throw 'git clone failed' }
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio Build Tools with C++ are required' }
$cmk = "$vs\Common7\IDE\CommonExtensions\Microsoft\CMake"

$opts = @(
    '-G', 'Ninja', '-DCMAKE_BUILD_TYPE=Release',
    '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded', '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW',
    '-DBUILD_SHARED_LIBS=OFF', '-DBUILD_TESTING=OFF',
    '-DJPEGXL_ENABLE_JPEGLI=ON', '-DJPEGXL_ENABLE_JPEGLI_LIBJPEG=OFF',
    '-DJPEGXL_ENABLE_TOOLS=OFF', '-DJPEGXL_ENABLE_BENCHMARK=OFF', '-DJPEGXL_ENABLE_EXAMPLES=OFF',
    '-DJPEGXL_ENABLE_MANPAGES=OFF', '-DJPEGXL_ENABLE_SJPEG=OFF', '-DJPEGXL_ENABLE_OPENEXR=OFF',
    '-DJPEGXL_ENABLE_DOXYGEN=OFF', '-DJPEGXL_ENABLE_JNI=OFF', '-DJPEGXL_ENABLE_VIEWERS=OFF',
    '-DJPEGXL_ENABLE_PLUGINS=OFF', '-DJPEGXL_ENABLE_TRANSCODE_JPEG=OFF', '-DJPEGXL_ENABLE_BOXES=OFF',
    '-DJPEGXL_ENABLE_FUZZERS=OFF', '-DJPEGXL_ENABLE_DEVTOOLS=OFF', '-DJPEGXL_ENABLE_SKCMS=ON',
    '-DJPEGXL_BUNDLE_LIBPNG=OFF'
) -join ' '

# Set up the MSVC x64 environment directly (vcvars64.bat isn't reliable on every machine).
$msvc = (Get-ChildItem "$vs\VC\Tools\MSVC" | Sort-Object Name | Select-Object -Last 1).FullName
$kits = "${env:ProgramFiles(x86)}\Windows Kits\10"
$sdk = Get-ChildItem "$kits\Include" | Where-Object { Test-Path "$($_.FullName)\um\windows.h" } | Sort-Object Name | Select-Object -Last 1 -ExpandProperty Name
if (-not $sdk) { throw 'Windows SDK is required' }
$env:INCLUDE = "$msvc\include;$kits\Include\$sdk\ucrt;$kits\Include\$sdk\um;$kits\Include\$sdk\shared;$kits\Include\$sdk\winrt"
$env:LIB = "$msvc\lib\x64;$kits\Lib\$sdk\ucrt\x64;$kits\Lib\$sdk\um\x64"
$env:PATH = "$msvc\bin\Hostx64\x64;$kits\bin\$sdk\x64;$cmk\CMake\bin;$cmk\Ninja;$env:PATH"

$cmake = "$cmk\CMake\bin\cmake.exe"
$ninja = "$cmk\Ninja\ninja.exe"
$cl = "$msvc\bin\Hostx64\x64\cl.exe"
foreach ($t in $cmake, $ninja, $cl) { if (-not (Test-Path $t)) { throw "not found: $t" } }

if (Test-Path "$out\CMakeCache.txt") { Remove-Item "$out\CMakeCache.txt" }
& $cmake -S $src -B $out "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_C_COMPILER=$cl" "-DCMAKE_CXX_COMPILER=$cl" ($opts -split ' ')
if ($LASTEXITCODE) { throw 'libjxl configure failed' }
& $cmake --build $out
if ($LASTEXITCODE) { throw 'libjxl build failed' }
& $cmake --build $out --target jpegli-static   # not part of the default build
if ($LASTEXITCODE) { throw 'jpegli build failed' }
Write-Host "Done. Now run build.ps1 - it will include ImgConvCodecs.dll."
