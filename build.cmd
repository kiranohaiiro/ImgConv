@echo off
cd /d "%~dp0"
if not exist dist mkdir dist
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC="%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /debug-
set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xaml.dll /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll"

%CSC% /out:dist\_tmp.exe %REFS% ImgConv.cs || exit /b 1
dist\_tmp.exe /mkico dist\ImgConv.ico
del dist\_tmp.exe

%CSC% /win32icon:dist\ImgConv.ico /out:dist\ImgConv.exe %REFS% ImgConv.cs || exit /b 1
%CSC% /win32icon:dist\ImgConv.ico /main:SetupApp /resource:dist\ImgConv.exe,ImgConv.exe /out:ImgConv-Setup.exe %REFS% Setup.cs ImgConv.cs || exit /b 1
del dist\ImgConv.ico
echo Done: ImgConv-Setup.exe
