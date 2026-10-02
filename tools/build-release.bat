@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

rem ============================================================
rem  ModsyncManager release build
rem
rem  Usage:
rem    build-release.bat
rem
rem  Результат:
rem    build_artifacts/ModsyncManager-<version>-win-x64.zip
rem
rem  Что внутри:
rem    ModsyncManager.exe            — GUI
rem    ModsyncManager.NxmHandler.exe — nxm:// handler
rem    *.dll                         — общие зависимости
rem    Assets/7z/                    — 7z.exe + 7z.dll + License.txt
rem
rem  Скрипт ожидает, что лежит в <repo>\tools\build-release.bat.
rem ============================================================

set "ROOT=%~dp0.."
pushd "%ROOT%" || (echo Failed to cd to "%ROOT%" & exit /b 1)

set "PROPS=Directory.Build.props"
set "GUI_PROJECT=src\ModsyncManager.Gui\ModsyncManager.Gui.csproj"
set "NXMHANDLER_PROJECT=src\ModsyncManager.NxmHandler\ModsyncManager.NxmHandler.csproj"

echo === ModsyncManager release build ===
echo Root: %CD%
echo.

rem --- 1. Читаем версию из Directory.Build.props ---
if not exist "%PROPS%" (
    echo ERROR: %PROPS% not found.
    popd
    exit /b 1
)

set "VERSION="
for /f "usebackq delims=" %%L in (`powershell -NoProfile -Command ^
    "(Select-Xml -Path '%PROPS%' -XPath '//VersionPrefix').Node.InnerText.Trim()"`) do (
    set "VERSION=%%L"
)

if "!VERSION!"=="" (
    echo ERROR: could not read ^<VersionPrefix^> from %PROPS%.
    popd
    exit /b 1
)

echo Version: !VERSION!
echo.

set "ARTIFACT_DIR=build_artifacts\ModsyncManager-!VERSION!-win-x64"
set "ARTIFACT_ZIP=build_artifacts\ModsyncManager-!VERSION!-win-x64.zip"

rem --- 2. Готовим пустую папку для publish ---
if exist "%ARTIFACT_DIR%" rmdir /s /q "%ARTIFACT_DIR%"
if exist "%ARTIFACT_ZIP%" del /q "%ARTIFACT_ZIP%"
mkdir "%ARTIFACT_DIR%" || (echo Failed to create "%ARTIFACT_DIR%" & popd & exit /b 1)

echo Publishing GUI: %GUI_PROJECT%
dotnet publish "%GUI_PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained false ^
    -o "%ARTIFACT_DIR%"
if errorlevel 1 (
    echo ERROR: dotnet publish failed for GUI.
    popd
    exit /b 1
)
echo.

echo Publishing NxmHandler: %NXMHANDLER_PROJECT%
dotnet publish "%NXMHANDLER_PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained false ^
    -o "%ARTIFACT_DIR%"
if errorlevel 1 (
    echo ERROR: dotnet publish failed for NxmHandler.
    popd
    exit /b 1
)
echo.

rem --- 3. Проверяем, что оба exe на месте ---
set "GUI_EXE=%ARTIFACT_DIR%\ModsyncManager.exe"
set "NXMHANDLER_EXE=%ARTIFACT_DIR%\ModsyncManager.NxmHandler.exe"

if not exist "%GUI_EXE%" (
    echo ERROR: %GUI_EXE% not found after publish.
    popd
    exit /b 1
)
if not exist "%NXMHANDLER_EXE%" (
    echo ERROR: %NXMHANDLER_EXE% not found after publish.
    popd
    exit /b 1
)

rem --- 4. Пакуем в zip ---
echo Packing: %ARTIFACT_ZIP%
powershell -NoProfile -Command ^
    "Compress-Archive -Path '%ARTIFACT_DIR%\*' -DestinationPath '%ARTIFACT_ZIP%' -Force"
if errorlevel 1 (
    echo ERROR: Compress-Archive failed.
    popd
    exit /b 1
)

rem --- 5. Итог ---
for %%F in ("%ARTIFACT_ZIP%") do set "ZIP_SIZE=%%~zF"
set /a ZIP_SIZE_MB=!ZIP_SIZE! / 1048576

echo.
echo Done.
echo Zip: %ARTIFACT_ZIP%
echo Size: !ZIP_SIZE! bytes (~!ZIP_SIZE_MB! MB)
echo.

popd
endlocal
exit /b 0