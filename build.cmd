@echo off
rem Builds TaskbarTimer.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:TaskbarTimer.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll TaskbarTimer.cs
