@echo off
rem Build with the C# compiler bundled with Windows (.NET Framework 4.x). No .NET SDK needed.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /out:bin\PaifuViewer.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll src\*.cs
if errorlevel 1 exit /b 1
echo Built bin\PaifuViewer.exe
