@echo off
title PC Locker Setup
cd /d "%~dp0"
echo ============================================
echo        PC LOCKER - Setup
echo ============================================
echo.
echo 1. Set password (first time)
echo 2. Lock now
echo 3. Change password
echo 4. Install to startup (auto-lock on boot)
echo 5. Remove from startup
echo 6. Exit
echo.
set /p choice="Choose [1-6]: "

if "%choice%"=="1" goto setpw
if "%choice%"=="2" goto lock
if "%choice%"=="3" goto changepw
if "%choice%"=="4" goto install
if "%choice%"=="5" goto uninstall
if "%choice%"=="6" exit
goto end

:setpw
python pclocker.py
goto end

:lock
python pclocker.py --lock
goto end

:changepw
python pclocker.py --change
goto end

:install
python pclocker.py --install
goto end

:uninstall
python pclocker.py --uninstall
goto end

:end
pause
