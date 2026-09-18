@echo off
setlocal
cd /d "%~dp0"

where py >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Python launcher "py" was not found.
  echo Music Fit requires Python 3.11, 3.12, or 3.13.
  echo Install Python 3.12 x64, then run this file again.
  pause
  exit /b 1
)

set "PY_CMD="

rem Prefer the CI-tested version first.
py -3.12 -c "import sys; raise SystemExit(0 if sys.version_info[:2] == (3,12) else 1)" >nul 2>nul
if not errorlevel 1 set "PY_CMD=py -3.12"

if not defined PY_CMD (
  py -3.13 -c "import sys; raise SystemExit(0 if sys.version_info[:2] == (3,13) else 1)" >nul 2>nul
  if not errorlevel 1 set "PY_CMD=py -3.13"
)

if not defined PY_CMD (
  py -3.11 -c "import sys; raise SystemExit(0 if sys.version_info[:2] == (3,11) else 1)" >nul 2>nul
  if not errorlevel 1 set "PY_CMD=py -3.11"
)

if not defined PY_CMD (
  echo.
  echo [ERROR] A supported Python version was not found.
  echo Music Fit requires Python 3.11, 3.12, or 3.13.
  echo Python 3.12 x64 is recommended.
  echo.
  echo Installed Python versions:
  py -0p
  echo.
  echo Install Python 3.12, then run setup_windows.bat again.
  pause
  exit /b 1
)

if exist ".venv\Scripts\python.exe" (
  ".venv\Scripts\python.exe" -c "import sys; raise SystemExit(0 if (3,11) <= sys.version_info[:2] <= (3,13) else 1)" >nul 2>nul
  if errorlevel 1 (
    echo Existing .venv uses an unsupported Python version. Recreating it...
    rmdir /s /q .venv
  )
)

if not exist ".venv\Scripts\python.exe" (
  echo Creating local virtual environment with %PY_CMD%...
  %PY_CMD% -m venv .venv
  if errorlevel 1 goto :fail
)

echo.
echo Using Python:
".venv\Scripts\python.exe" --version

".venv\Scripts\python.exe" -c "import sys; raise SystemExit(0 if (3,11) <= sys.version_info[:2] <= (3,13) else 1)"
if errorlevel 1 (
  echo [ERROR] The virtual environment is using an unsupported Python version.
  goto :fail
)

echo.
echo Installing Music Fit CPU dependencies...
".venv\Scripts\python.exe" -m pip install --upgrade pip
if errorlevel 1 goto :fail
".venv\Scripts\python.exe" -m pip install -r requirements.txt
if errorlevel 1 goto :fail

if not exist input mkdir input
if not exist output mkdir output

echo.
echo Setup completed.
echo Put BGM files into the "input" folder, edit settings.json if needed,
echo then run run_windows.bat.
pause
exit /b 0

:fail
echo.
echo [ERROR] Setup failed.
pause
exit /b 1
