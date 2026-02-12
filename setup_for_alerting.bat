@echo off
setlocal

REM --- Root paths ---
set "APP_ROOT=%~dp0"
set "SERVER_ROOT=%APP_ROOT%Server"
set "ENV_ROOT=%SERVER_ROOT%\epysurv-dev"

REM --- Activate conda-pack env (sets CONDA_PREFIX + PATH properly) ---
call "%ENV_ROOT%\Scripts\activate.bat"

REM --- R installation (needed for rpy2) ---
REM Use the bundled R under the app directory
set "R_HOME=%ENV_ROOT%\Lib\R"

REM Add R to PATH (bin and bin\x64 if present)
set "PATH=%R_HOME%\bin\x64;%R_HOME%\bin;%PATH%"

REM --- Move to Server folder (relative paths inside code behave) ---
cd /d "%SERVER_ROOT%"

echo Starting backend...
start "ForeSITE Backend" cmd /k ""%ENV_ROOT%\python.exe" "epyflaServer.py""

REM --- Wait for backend to bind port ---
timeout /t 4 >nul

REM --- Start GUI ---
cd /d "%APP_ROOT%"
start "" "ForeSITETestApp.exe"