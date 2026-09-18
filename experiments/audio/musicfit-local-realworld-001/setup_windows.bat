@echo off
setlocal
cd /d "%~dp0"

where py >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Python launcher "py" was not found.
  echo Install Python 3.12 or newer, then run this file again.
  pause
  exit /b 1
)

if not exist ".venv\Scripts\python.exe" (
  echo Creating local virtual environment...
  py -3.12 -m venv .venv
  if errorlevel 1 (
    echo Python 3.12 was not available. Trying the default Python...
    py -m venv .venv
    if errorlevel 1 goto :fail
  )
)

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
