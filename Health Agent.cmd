@echo off
rem Starts the local server and opens the app in its own window.
cd /d "%~dp0"
start "Health Agent server" /min cmd /c "node server.mjs"
timeout /t 2 /nobreak >nul
start "" msedge --app=http://localhost:4747
