@echo off
rem Abre o Juicebar na barra de tarefas. Na primeira vez (ou se o .cs mudar) compila
rem o juicebar.exe em %LOCALAPPDATA%\Juicebar com o compilador do Windows.
setlocal
set "SRC=%~dp0juicebar.cs"
set "OUT=%LOCALAPPDATA%\Juicebar"
set "EXE=%OUT%\juicebar.exe"
if not exist "%OUT%" mkdir "%OUT%"

set "PRECISA="
if not exist "%EXE%" set PRECISA=1
if exist "%EXE%" (
  for /f %%r in ('powershell -NoProfile -Command "if ((Get-Item '%SRC%').LastWriteTime -gt (Get-Item '%EXE%').LastWriteTime) {1} else {0}"') do if "%%r"=="1" set PRECISA=1
)

if defined PRECISA (
  set "NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
  set "WMD=%WINDIR%\System32\WinMetadata"
  set "GAC=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL"
  call :compilar || (echo Nao consegui compilar. & pause & exit /b 1)
)

start "" "%EXE%" %*
exit /b 0

:compilar
set "R="
for %%n in (System.Runtime System.Runtime.InteropServices.WindowsRuntime System.Threading.Tasks System.ObjectModel) do (
  for /d %%d in ("%GAC%\%%n\*") do if exist "%%d\%%n.dll" call set "R=%%R%% -r:"%%d\%%n.dll""
)
"%NET%\csc.exe" -nologo -codepage:65001 -nowarn:162 -target:winexe -out:"%EXE%" -win32icon:"%~dp0juicebar.ico" ^
  -r:"%WMD%\Windows.Devices.winmd" -r:"%WMD%\Windows.Foundation.winmd" -r:"%WMD%\Windows.Storage.winmd" ^
  -r:"%NET%\System.Runtime.WindowsRuntime.dll" -r:System.Windows.Forms.dll -r:System.Drawing.dll %R% "%SRC%"
exit /b %errorlevel%
