@echo off
setlocal
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
  echo [ERROR] Setup has not been completed.
  echo Run setup_windows.bat first.
  pause
  exit /b 1
)

if not exist input mkdir input
if not exist output mkdir output

".venv\Scripts\python.exe" run_local_test.py --settings "settings.phase5.json"
set EXITCODE=%ERRORLEVEL%

echo.
if %EXITCODE%==0 (
  echo Local Music Fit test completed.
) else (
  echo [ERROR] Local Music Fit test ended with exit code %EXITCODE%.
)
pause
exit /b %EXITCODE%
