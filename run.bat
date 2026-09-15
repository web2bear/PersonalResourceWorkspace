@echo off
setlocal

pushd "%~dp0" || exit /b 1

set "APP_NAME=PersonalResourceWorkspace.App.exe"
set "APP_PROJECT=src\PersonalResourceWorkspace.App\PersonalResourceWorkspace.App.csproj"
set "APP_DIR=%CD%\src\PersonalResourceWorkspace.App\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64"
set "APP_EXE=%APP_DIR%\%APP_NAME%"

tasklist /FI "IMAGENAME eq %APP_NAME%" 2>nul | find /I "%APP_NAME%" >nul
if not errorlevel 1 (
    echo Personal Resource Workspace is already running.
    popd
    exit /b 0
)

if /I not "%~1"=="--build" if exist "%APP_EXE%" goto launch

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: .NET SDK was not found in PATH.
    pause
    popd
    exit /b 1
)

echo Building Personal Resource Workspace...
dotnet build "%APP_PROJECT%" --configuration Debug --runtime win-x64 --no-restore -p:Platform=x64
if errorlevel 1 (
    echo.
    echo Build failed. Review the errors above.
    pause
    popd
    exit /b 1
)

if not exist "%APP_EXE%" (
    echo Error: application executable was not produced at "%APP_EXE%".
    pause
    popd
    exit /b 1
)

:launch
pushd "%APP_DIR%" || (
    echo Error: application output directory was not found.
    pause
    popd
    exit /b 1
)
start "" "%APP_NAME%"
set "LAUNCH_EXIT=%ERRORLEVEL%"
popd
if not "%LAUNCH_EXIT%"=="0" (
    echo Error: application launch failed.
    pause
    popd
    exit /b 1
)

popd
exit /b 0
