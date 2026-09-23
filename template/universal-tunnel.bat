@echo off
title Universal Multi-Project Tunnel Manager
color 0b

:MENU
cls
echo =====================================================================
echo              UNIVERSAL MULTI-PROJECT TUNNEL MANAGER
echo            (Expose Any Local Project to the Internet)
echo =====================================================================
echo.
echo   Client Requirements: NONE (Zero install, opens in any browser)
echo.
echo   Select a project / port to tunnel:
echo.
echo     [1] Loan-System (Port 5287)
echo     [2] Next.js / React (Port 3000)
echo     [3] Vite / Vue / Svelte (Port 5173)
echo     [4] ASP.NET Core Default (Port 5000)
echo     [5] Python / Django / FastAPI (Port 8000)
echo     [6] Scan active listening ports on this PC
echo     [7] Enter custom port number
echo.
echo     [8] Exit
echo.
echo =====================================================================
set /p OPT="Enter your choice (1-8): "

if "%OPT%"=="1" set TARGET_PORT=5287 & goto CHOOSE_PROVIDER
if "%OPT%"=="2" set TARGET_PORT=3000 & goto CHOOSE_PROVIDER
if "%OPT%"=="3" set TARGET_PORT=5173 & goto CHOOSE_PROVIDER
if "%OPT%"=="4" set TARGET_PORT=5000 & goto CHOOSE_PROVIDER
if "%OPT%"=="5" set TARGET_PORT=8000 & goto CHOOSE_PROVIDER
if "%OPT%"=="6" goto SCAN_PORTS
if "%OPT%"=="7" goto CUSTOM_PORT
if "%OPT%"=="8" goto EXIT_SCRIPT

echo [ERROR] Invalid selection.
timeout /t 2 >nul
goto MENU


:: ========================================================================
:: SCAN RUNNING PORTS ON PC
:: ========================================================================
:SCAN_PORTS
cls
echo =====================================================================
echo                  SCANNING ACTIVE LOCAL PORTS
echo =====================================================================
echo.
echo Searching for web servers currently running on your PC...
echo.
powershell -NoProfile -Command ^
  "$ports = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { ($_.LocalAddress -eq '127.0.0.1' -or $_.LocalAddress -eq '0.0.0.0') -and $_.LocalPort -ge 1000 -and $_.LocalPort -le 65000 } | Select-Object -ExpandProperty LocalPort -Unique | Sort-Object; if ($ports) { Write-Host 'Active local ports found:' -ForegroundColor Green; foreach ($p in $ports) { Write-Host ('  - Port ' + $p) } } else { Write-Host 'No active listening ports detected.' -ForegroundColor Yellow }"

echo.
set /p TARGET_PORT="Type the port number you want to tunnel: "
if "%TARGET_PORT%"=="" goto MENU
goto CHOOSE_PROVIDER


:: ========================================================================
:: ENTER CUSTOM PORT
:: ========================================================================
:CUSTOM_PORT
echo.
set /p TARGET_PORT="Enter the local port number (e.g. 8080, 4200, 5287): "
if "%TARGET_PORT%"=="" goto MENU
goto CHOOSE_PROVIDER


:: ========================================================================
:: CHOOSE TUNNEL PROVIDER (CLOUDFLARE vs NGROK)
:: ========================================================================
:CHOOSE_PROVIDER
cls
echo =====================================================================
echo            SELECT TUNNEL PROVIDER FOR PORT: %TARGET_PORT%
echo =====================================================================
echo.
echo   [1] Cloudflare Tunnel (Recommended)
echo       - 100%% Free, No account required, No authtoken needed.
echo       - Auto-downloads portable binary if not installed.
echo.
echo   [2] Ngrok
echo       - Popular and fast. Requires free Ngrok account / Authtoken.
echo       - Auto-downloads and extracts binary if not installed.
echo.
echo   [3] Back to Main Menu
echo.
echo =====================================================================
set /p PROV_CHOICE="Select provider (1 or 2): "

if "%PROV_CHOICE%"=="1" goto PREPARE_CLOUDFLARE
if "%PROV_CHOICE%"=="2" goto PREPARE_NGROK
if "%PROV_CHOICE%"=="3" goto MENU

echo [ERROR] Invalid choice.
timeout /t 2 >nul
goto CHOOSE_PROVIDER


:: ========================================================================
:: PROVIDER 1: CLOUDFLARE TUNNEL
:: ========================================================================
:PREPARE_CLOUDFLARE
cls
echo =====================================================================
echo               STARTING CLOUDFLARE TUNNEL (Port %TARGET_PORT%)
echo =====================================================================
echo.

if exist "cloudflared.exe" (
    set CF_CMD=.\cloudflared.exe
    goto LAUNCH_CLOUDFLARE
)

where cloudflared >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    set CF_CMD=cloudflared
    goto LAUNCH_CLOUDFLARE
)

echo [INFO] cloudflared.exe not detected on your system.
echo [INSTALL] Auto-downloading portable cloudflared (approx 35MB)...
echo           Source: Cloudflare Official GitHub Releases
echo.
powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe' -OutFile 'cloudflared.exe'"

if not exist "cloudflared.exe" (
    echo [ERROR] Failed to download cloudflared.exe. Please check your internet connection.
    pause
    goto CHOOSE_PROVIDER
)

echo [OK] cloudflared.exe installed successfully!
set CF_CMD=.\cloudflared.exe

:LAUNCH_CLOUDFLARE
echo.
echo [OK] Connecting Cloudflare Tunnel to http://localhost:%TARGET_PORT% ...
echo =====================================================================
echo Look below for your public link ending with:
echo    https://xxxx.trycloudflare.com
echo.
echo Share that link with your client.
echo (Press Ctrl+C or close this window to STOP access)
echo =====================================================================
echo.

%CF_CMD% tunnel --url http://localhost:%TARGET_PORT%

echo.
echo Cloudflare Tunnel closed.
pause
goto MENU


:: ========================================================================
:: PROVIDER 2: NGROK
:: ========================================================================
:PREPARE_NGROK
cls
echo =====================================================================
echo                  STARTING NGROK (Port %TARGET_PORT%)
echo =====================================================================
echo.

if exist "ngrok.exe" (
    set NGROK_CMD=.\ngrok.exe
    goto CHECK_NGROK_AUTH
)

where ngrok >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    set NGROK_CMD=ngrok
    goto CHECK_NGROK_AUTH
)

echo [INFO] ngrok.exe not detected on your system.
echo [INSTALL] Auto-downloading official Ngrok archive...
echo           Source: bin.equinox.io (Official Ngrok v3 Stable)
echo.
powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-windows-amd64.zip' -OutFile 'ngrok.zip'; Expand-Archive -Path 'ngrok.zip' -DestinationPath '.' -Force; Remove-Item 'ngrok.zip' -Force"

if not exist "ngrok.exe" (
    echo [ERROR] Failed to download/extract ngrok.exe. Please check your internet connection.
    pause
    goto CHOOSE_PROVIDER
)

echo [OK] ngrok.exe installed successfully!
set NGROK_CMD=.\ngrok.exe

:CHECK_NGROK_AUTH
echo.
echo Note: Ngrok requires a free account authtoken (from dashboard.ngrok.com).
echo If you have already added your authtoken before, press ENTER to skip.
echo Otherwise, paste your authtoken below:
set /p NGROK_TOKEN="Paste Ngrok Authtoken (or press Enter to skip): "

if not "%NGROK_TOKEN%"=="" (
    echo Configuring Ngrok authtoken...
    %NGROK_CMD% config add-authtoken %NGROK_TOKEN%
)

echo.
echo [OK] Connecting Ngrok to http://localhost:%TARGET_PORT% ...
echo =====================================================================
echo Look at the Ngrok UI below for your Forwarding URL (https://xxxx.ngrok-free.app).
echo (Press Ctrl+C or close this window to STOP access)
echo =====================================================================
echo.

%NGROK_CMD% http %TARGET_PORT%

echo.
echo Ngrok Tunnel closed.
pause
goto MENU


:: ========================================================================
:: EXIT
:: ========================================================================
:EXIT_SCRIPT
echo Exiting Universal Tunnel Manager.
exit /b 0
