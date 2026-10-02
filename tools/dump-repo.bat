@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

rem ============================================================
rem  ModsyncManager repo dump
rem
rem  Usage:
rem    dump-repo.bat                — оба дампа (main + extra)
rem    dump-repo.bat main           — только repo-dump.md
rem    dump-repo.bat extra          — только repo-dump-extra.md
rem
rem  Файлы кладутся в корень репозитория (рядом с .git).
rem  Скрипт ожидает, что лежит в <repo>\tools\dump-repo.bat.
rem ============================================================

set "ROOT=%~dp0.."
pushd "%ROOT%" || (echo Failed to cd to "%ROOT%" & exit /b 1)

set "MODE=%~1"
if "%MODE%"=="" set "MODE=both"

set "OUT_MAIN=%CD%\repo-dump.md"
set "OUT_EXTRA=%CD%\repo-dump-extra.md"

echo === ModsyncManager repo dump ===
echo Root: %CD%
echo Mode: %MODE%
echo.

if /i "%MODE%"=="main"  call :dump_main
if /i "%MODE%"=="extra" call :dump_extra
if /i "%MODE%"=="both"  (
    call :dump_main
    call :dump_extra
)

echo Done.
popd
endlocal
exit /b 0

rem ============================================================
rem  MAIN — только код и критичные конфиги
rem ============================================================
:dump_main
echo.
echo [MAIN] -^> %OUT_MAIN%
if exist "%OUT_MAIN%" del "%OUT_MAIN%"
call :write_header "%OUT_MAIN%"

set "TEMP_LIST=%TEMP%\modsyncmanager-main-%RANDOM%-%RANDOM%.txt"

rem 1) основные файлы: код + проекты + конфиги сборки + доки
dir /b /s /a-d "%CD%" ^
  | findstr /v /i "\.git\ \.vs\ \.idea\ \.vscode\ \bin\ \obj\ \packages\ \node_modules\ \TestResults\ \coverage\ \.nuget\ \Debug\ \Release\ \Assets\7z\" ^
  | findstr /i /e ".editorconfig .gitignore .gitattributes .yml .yaml .ps1 .psm1 .bat .cmd .sh .xml .config .txt .json .axaml .cs" ^
  > "%TEMP_LIST%"

rem 2) samples/*.json — обязательно включаем
if exist "%CD%\samples" (
    dir /b /s /a-d "%CD%\samples" ^
      | findstr /i /e ".json" ^
      >> "%TEMP_LIST%"
)

rem 3) выкидываем автогенерённые .cs
findstr /v /i /e "AssemblyInfo.cs AssemblyAttributes.cs GlobalUsings.g.cs" "%TEMP_LIST%" > "%TEMP_LIST%.f"
del "%TEMP_LIST%" >nul 2>&1
move /y "%TEMP_LIST%.f" "%TEMP_LIST%" >nul

call :write_files "%OUT_MAIN%" "%TEMP_LIST%"

del "%TEMP_LIST%" >nul 2>&1
exit /b 0

rem ============================================================
rem  EXTRA — всё остальное, что может пригодиться по запросу
rem ============================================================
:dump_extra
echo.
echo [EXTRA] -^> %OUT_EXTRA%
if exist "%OUT_EXTRA%" del "%OUT_EXTRA%"
call :write_header "%OUT_EXTRA%"

set "TEMP_LIST=%TEMP%\modsyncmanager-extra-%RANDOM%-%RANDOM%.txt"

dir /b /s /a-d "%CD%" ^
  | findstr /v /i "\.git\ \.vs\ \.idea\ \.vscode\ \bin\ \obj\ \packages\ \node_modules\ \TestResults\ \coverage\ \.nuget\ \Debug\ \Release\ \Assets\7z\" ^
  | findstr /i /e ".editorconfig .gitignore .gitattributes .yml .yaml .ps1 .psm1 .bat .cmd .sh .xml .config .txt .json .csproj .props" ^
  > "%TEMP_LIST%"

rem выкидываем мусор от nuget/msbuild
findstr /v /i /e ".deps.json .runtimeconfig.json .sourcelink.json .nuget.g.props .nuget.g.targets .dgspec.json project.assets.json" "%TEMP_LIST%" > "%TEMP_LIST%.f"
del "%TEMP_LIST%" >nul 2>&1
move /y "%TEMP_LIST%.f" "%TEMP_LIST%" >nul

rem не включаем сам дамп и скрипт
findstr /v /i /e "repo-dump.md repo-dump-extra.md dump-repo.bat" "%TEMP_LIST%" > "%TEMP_LIST%.f"
del "%TEMP_LIST%" >nul 2>&1
move /y "%TEMP_LIST%.f" "%TEMP_LIST%" >nul

call :write_files "%OUT_EXTRA%" "%TEMP_LIST%"

del "%TEMP_LIST%" >nul 2>&1
exit /b 0

rem ============================================================
rem  Хелперы
rem ============================================================

:write_header
(
  echo # ModsyncManager -- repo dump
  echo.
  echo **Generated:** %DATE% %TIME%
  echo **Root:** %CD%
  echo.
  echo ---
  echo.
) >> "%~1"
exit /b 0

:write_files
set "OUT_FILE=%~1"
set "LIST_FILE=%~2"

set COUNT=0
for /f "usebackq delims=" %%F in ("%LIST_FILE%") do set /a COUNT+=1

echo Files to include: %COUNT%

set /a INDEX=0

for /f "usebackq delims=" %%F in ("%LIST_FILE%") do (
    set /a INDEX+=1
    set "FULL=%%F"
    set "REL=!FULL:%CD%\=!"
    set "REL=!REL:\=/!"

    echo [!INDEX!/%COUNT%] !REL!

    echo ## !REL!>> "%OUT_FILE%"
    echo.>> "%OUT_FILE%"

    set "EXT=%%~xF"
    set "LANG=text"
    if /i "!EXT!"==".cs"            set "LANG=csharp"
    if /i "!EXT!"==".csproj"        set "LANG=xml"
    if /i "!EXT!"==".slnx"          set "LANG=xml"
    if /i "!EXT!"==".props"         set "LANG=xml"
    if /i "!EXT!"==".targets"       set "LANG=xml"
    if /i "!EXT!"==".json"          set "LANG=json"
    if /i "!EXT!"==".md"            set "LANG=markdown"
    if /i "!EXT!"==".ps1"           set "LANG=powershell"
    if /i "!EXT!"==".psm1"          set "LANG=powershell"
    if /i "!EXT!"==".yml"           set "LANG=yaml"
    if /i "!EXT!"==".yaml"          set "LANG=yaml"
    if /i "!EXT!"==".xml"           set "LANG=xml"
    if /i "!EXT!"==".config"        set "LANG=xml"
    if /i "!EXT!"==".bat"           set "LANG=batch"
    if /i "!EXT!"==".cmd"           set "LANG=batch"
    if /i "!EXT!"==".sh"            set "LANG=bash"

    echo ````!LANG!>> "%OUT_FILE%"
    type "%%F" >> "%OUT_FILE%"
    echo.>> "%OUT_FILE%"
    echo ````>> "%OUT_FILE%"
    echo.>> "%OUT_FILE%"
)
exit /b 0