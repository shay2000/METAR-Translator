@echo off
setlocal EnableExtensions

set "INTEGRATION_ROOT=%~dp0"
set "PROJECT_FILE=%INTEGRATION_ROOT%MetarViewerToolbar.xml"
set "SOURCE_VALIDATOR=%INTEGRATION_ROOT%tools\validate-source.mjs"
set "PACKAGE_VALIDATOR=%INTEGRATION_ROOT%tools\validate-package.mjs"
set "ZIP_CREATOR=%INTEGRATION_ROOT%tools\create-package-zip.ps1"
set "PACKAGE_OUTPUT=%INTEGRATION_ROOT%Packages\metar-viewer-toolbar"
set "PACKAGE_ZIP=%INTEGRATION_ROOT%Packages\metar-viewer-toolbar.zip"

if not "%~1"=="" set "MSFS_SDK=%~1"

if not defined NODE_EXE set "NODE_EXE=node"

"%NODE_EXE%" --version >nul 2>&1
if errorlevel 1 (
  echo ERROR: Node.js was not found.
  echo Install Node.js 24 LTS or newer, or set NODE_EXE to the full path to node.exe.
  exit /b 1
)

for /f "tokens=1 delims=." %%V in ('"%NODE_EXE%" -p "process.versions.node" 2^>nul') do set "NODE_MAJOR=%%V"
if not defined NODE_MAJOR (
  echo ERROR: Could not determine the Node.js version.
  exit /b 1
)
if %NODE_MAJOR% LSS 24 (
  echo ERROR: Node.js 24 LTS or newer is required; found major version %NODE_MAJOR%.
  exit /b 1
)

echo [1/4] Validating MSFS package sources...
"%NODE_EXE%" "%SOURCE_VALIDATOR%" "%INTEGRATION_ROOT%"
if errorlevel 1 exit /b 1

if not defined MSFS_SDK (
  echo ERROR: MSFS_SDK is not set.
  echo Set MSFS_SDK to the Microsoft Flight Simulator 2020 SDK installation directory,
  echo or pass that directory as the first argument to build.bat.
  exit /b 1
)

set "PACKAGE_TOOL=%MSFS_SDK%\Tools\bin\fspackagetool.exe"

if not exist "%PACKAGE_TOOL%" (
  echo ERROR: Package Tool was not found at:
  echo   "%PACKAGE_TOOL%"
  echo Check MSFS_SDK and install the MSFS 2020 SDK from Developer Mode.
  exit /b 1
)

if not exist "%PROJECT_FILE%" (
  echo ERROR: Project file was not found at "%PROJECT_FILE%".
  exit /b 1
)

if not exist "%ZIP_CREATOR%" (
  echo ERROR: ZIP helper was not found at "%ZIP_CREATOR%".
  exit /b 1
)

echo [2/4] Building with the MSFS 2020 Package Tool...
pushd "%INTEGRATION_ROOT%"
if errorlevel 1 (
  echo ERROR: Could not enter "%INTEGRATION_ROOT%".
  exit /b 1
)

"%PACKAGE_TOOL%" "%PROJECT_FILE%" -rebuild -mirroring -nopause
set "PACKAGE_TOOL_EXIT=%ERRORLEVEL%"
if not "%PACKAGE_TOOL_EXIT%"=="0" goto :package_tool_failed
popd

if not exist "%PACKAGE_OUTPUT%\manifest.json" (
  echo ERROR: Package Tool completed without producing the expected package:
  echo   "%PACKAGE_OUTPUT%"
  exit /b 1
)

echo [3/4] Validating the built Community package...
"%NODE_EXE%" "%PACKAGE_VALIDATOR%" "%PACKAGE_OUTPUT%"
if errorlevel 1 exit /b 1

echo [4/4] Creating the Community-folder ZIP...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ZIP_CREATOR%" -PackagePath "%PACKAGE_OUTPUT%" -OutputPath "%PACKAGE_ZIP%"
if errorlevel 1 (
  echo ERROR: Could not create the Community-folder ZIP.
  exit /b 1
)

echo.
echo Build and structural validation completed successfully.
echo Package folder: "%PACKAGE_OUTPUT%"
echo Distribution ZIP: "%PACKAGE_ZIP%"
echo REQUIRED RELEASE GATE: install this package in a clean Community folder and
echo verify the icon, panel lifecycle, network behavior, and console output in MSFS 2020.
exit /b 0

:package_tool_failed
popd
echo ERROR: MSFS Package Tool failed with exit code %PACKAGE_TOOL_EXIT%.
exit /b %PACKAGE_TOOL_EXIT%
