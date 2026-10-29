# Builds ImgConv-Setup.exe.
#   powershell -ExecutionPolicy Bypass -File build.ps1
#
# Always: the app + installer, with the C# compiler that ships with Windows.
# If Visual Studio Build Tools (C++) and the Windows SDK are installed, also the
# Windows 11 context menu: ImgConvMenu.dll + a sparse MSIX signed with a
# self-signed certificate "CN=ImgConv". The certificate is created once in your
# personal store (Cert:\CurrentUser\My); its private key never leaves this PC.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
function Csc([string[]]$a) {
    & "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /debug- `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xaml.dll `
        "/r:$fw\WPF\PresentationCore.dll" "/r:$fw\WPF\WindowsBase.dll" @a
    if ($LASTEXITCODE) { throw 'C# build failed' }
}

# 1. The app. A temporary build draws the icon and the package logo.
Csc "/out:$dist\_tmp.exe", 'ImgConv.cs'
Start-Process "$dist\_tmp.exe" -ArgumentList "/mkico `"$dist\ImgConv.ico`"" -Wait
Start-Process "$dist\_tmp.exe" -ArgumentList "/mkpng `"$dist\pkg\Assets\Logo.png`" 150" -Wait
Remove-Item "$dist\_tmp.exe"
Csc "/win32icon:$dist\ImgConv.ico", "/out:$dist\ImgConv.exe", 'ImgConv.cs'
$res = @("/resource:$dist\ImgConv.exe,ImgConv.exe")

# 2. Windows 11 menu (optional)
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
$kits = "${env:ProgramFiles(x86)}\Windows Kits\10"
$sdk = if (Test-Path "$kits\Include") { Get-ChildItem "$kits\Include" | Where-Object { Test-Path "$($_.FullName)\um\windows.h" } | Sort-Object Name | Select-Object -Last 1 -ExpandProperty Name }

if ($vs -and $sdk -and (Test-Path "$kits\bin\$sdk\x64\makeappx.exe")) {
    $msvc = (Get-ChildItem "$vs\VC\Tools\MSVC" | Sort-Object Name | Select-Object -Last 1).FullName
    $env:INCLUDE = "$msvc\include;$kits\Include\$sdk\ucrt;$kits\Include\$sdk\um;$kits\Include\$sdk\shared;$kits\Include\$sdk\winrt"
    $env:LIB = "$msvc\lib\x64;$kits\Lib\$sdk\ucrt\x64;$kits\Lib\$sdk\um\x64"
    & "$msvc\bin\Hostx64\x64\cl.exe" /nologo /utf-8 /O1 /MT /EHsc /std:c++17 /LD /DUNICODE /D_UNICODE `
        menu\ImgConvMenu.cpp "/Fe:$dist\ImgConvMenu.dll" "/Fo:$dist\\" `
        /link /DEF:menu\ImgConvMenu.def shlwapi.lib ole32.lib advapi32.lib shell32.lib user32.lib runtimeobject.lib | Out-Null
    if ($LASTEXITCODE) { throw 'C++ build failed' }

    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq 'CN=ImgConv' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1
    if (-not $cert) {
        Write-Host 'Creating signing certificate CN=ImgConv (one time)...'
        $cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=ImgConv' -FriendlyName 'ImgConv package signing' `
            -KeyUsage DigitalSignature -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    Export-Certificate -Cert $cert -FilePath "$dist\ImgConv.cer" | Out-Null

    Copy-Item menu\AppxManifest.xml "$dist\pkg\AppxManifest.xml"
    & "$kits\bin\$sdk\x64\makeappx.exe" pack /o /nv /d "$dist\pkg" /p "$dist\ImgConv.msix" | Out-Null
    if ($LASTEXITCODE) { throw 'makeappx failed' }
    & "$kits\bin\$sdk\x64\signtool.exe" sign /q /fd SHA256 /sha1 $cert.Thumbprint "$dist\ImgConv.msix"
    if ($LASTEXITCODE) { throw 'signtool failed' }

    $res += "/resource:$dist\ImgConvMenu.dll,ImgConvMenu.dll", "/resource:$dist\ImgConv.msix,ImgConv.msix", "/resource:$dist\ImgConv.cer,ImgConv.cer"

    # Optional codecs (jpegli + JPEG XL): needs the libjxl libraries from build-codecs.ps1.
    $jxl = Join-Path $env:LOCALAPPDATA 'ImgConvBuild'
    $need = 'jpegli-static.lib', 'jxl_dec.lib', 'jxl_cms.lib', 'hwy.lib', 'brotlidec.lib', 'brotlicommon.lib'
    $libs = if (Test-Path "$jxl\out") { Get-ChildItem "$jxl\out" -Recurse -Include $need }
    if ($libs -and @($libs).Count -eq $need.Count) {
        $incs = @("$jxl\libjxl", "$jxl\libjxl\lib\include", "$jxl\out\lib\include", "$jxl\libjxl\third_party\libjpeg-turbo", "$jxl\libjxl\third_party\highway")
        $incs += Get-ChildItem "$jxl\out" -Recurse -Filter jconfig.h | ForEach-Object { $_.DirectoryName } | Select-Object -Unique
        $env:INCLUDE = (($incs | ForEach-Object { $_ }) -join ';') + ';' + $env:INCLUDE
        & "$msvc\bin\Hostx64\x64\cl.exe" /nologo /utf-8 /O2 /MT /EHsc /std:c++17 /LD /DJXL_STATIC_DEFINE /DJXL_THREADS_STATIC_DEFINE `
            codecs\ImgConvCodecs.cpp "/Fe:$dist\ImgConvCodecs.dll" "/Fo:$dist\\" `
            /link /OPT:REF /OPT:ICF ($libs | ForEach-Object { $_.FullName }) | Select-String -Pattern 'error|warning LNK' | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE) { throw 'ImgConvCodecs build failed' }
        $res += "/resource:$dist\ImgConvCodecs.dll,ImgConvCodecs.dll", "/resource:THIRD-PARTY-NOTICES.txt,THIRD-PARTY-NOTICES.txt"
    } else {
        Write-Host 'libjxl not built (run build-codecs.ps1 once): building without jpegli / JPEG XL.'
    }
} else {
    Write-Host 'C++ Build Tools or Windows SDK not found: building without the Windows 11 menu.'
}

# 3. Installer with everything embedded (gzip-compressed; Setup.cs unpacks on install)
New-Item -ItemType Directory "$dist\gz" -Force | Out-Null
$gz = foreach ($r in $res) {
    $file, $name = ($r -replace '^/resource:', '') -split ',', 2
    $target = "$dist\gz\$name"
    $in = [IO.File]::OpenRead($file); $outStream = [IO.File]::Create($target)
    $z = New-Object IO.Compression.GZipStream($outStream, [IO.Compression.CompressionMode]::Compress)
    $in.CopyTo($z); $z.Dispose(); $outStream.Dispose(); $in.Dispose()
    "/resource:$target,$name"
}
Csc (@("/win32icon:$dist\ImgConv.ico", '/main:SetupApp', '/out:ImgConv-Setup.exe') + $gz + @('Setup.cs', 'ImgConv.cs'))
Write-Host ('Done: ImgConv-Setup.exe ({0:N0} KB)' -f ((Get-Item ImgConv-Setup.exe).Length / 1KB))
