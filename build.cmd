@echo off
setlocal
pushd "%~dp0" || exit /b 1
set "csc=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%csc%" /nologo /optimize+ /codepage:65001 /target:exe /out:Convert.exe /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll src\Converter.cs src\Disc.cs src\Packages.cs
if errorlevel 1 exit /b 1
"%csc%" /nologo /optimize+ /codepage:65001 /target:winexe "/out:portable\Display Settings.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\RacerDisplay.cs
exit /b %errorlevel%
