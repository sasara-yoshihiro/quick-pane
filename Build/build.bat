@echo off
rem ============================================================
rem  QuickPane build script
rem    Output : Build\QuickPane\
rem    Usage  : build.bat [win-x64 ^| win-arm64]   (default: win-x64)
rem ============================================================
setlocal

set "SCRIPT_DIR=%~dp0"
set "ROOT=%SCRIPT_DIR%.."
set "OUT=%SCRIPT_DIR%QuickPane"
set "CONFIGURATION=Release"

set "RID=%~1"
if "%RID%"=="" set "RID=win-x64"
if /i "%RID%"=="win-x64" (
    set "WINUI_PLATFORM=x64"
) else if /i "%RID%"=="win-arm64" (
    set "WINUI_PLATFORM=ARM64"
) else (
    echo [ERROR] Unsupported runtime identifier: %RID%
    echo         Use win-x64 or win-arm64.
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet command not found. Install the .NET 10 SDK.
    exit /b 1
)

tasklist /fi "imagename eq QuickPane.exe" 2>nul | find /i "QuickPane.exe" >nul
if not errorlevel 1 (
    echo [ERROR] QuickPane is running. Exit QuickPane from the tray icon menu and try again.
    exit /b 1
)

rem Keep user data (Data / Log) when rebuilding into an existing output folder
echo === Clean output: %OUT%
if exist "%OUT%" (
    for /d %%D in ("%OUT%\*") do (
        if /i not "%%~nxD"=="Data" if /i not "%%~nxD"=="Log" rmdir /s /q "%%D"
    )
    del /q "%OUT%\*" >nul 2>&1
)

echo.
echo === Publish QuickPane (WinUI 3) [%RID%]
dotnet publish "%ROOT%\QuickPane\QuickPane.csproj" -c %CONFIGURATION% -r %RID% -p:Platform=%WINUI_PLATFORM% --self-contained false -o "%OUT%" -p:DebugType=none -nologo
if errorlevel 1 goto :error

if not exist "%OUT%\Log" mkdir "%OUT%\Log"

echo.
echo === Build succeeded: %OUT%
exit /b 0

:error
echo.
echo [ERROR] Build failed.
exit /b 1
