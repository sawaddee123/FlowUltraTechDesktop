@echo off
title FlowUltraTech Launcher
echo ===================================================
echo   TikTok LIVE Studio Sandbox Manager Launcher
echo ===================================================
echo.
echo Stopping any existing instances...
taskkill /F /IM FlowUltraTech.App.exe 2>nul
echo.
if exist "%~dp0dist\publish\FlowUltraTech.App.exe" (
    echo Launching Published Release version...
    start "" /D "%~dp0dist\publish" "%~dp0dist\publish\FlowUltraTech.App.exe"
) else (
    echo Launching Debug version...
    start "" /D "%~dp0src\FlowUltraTech.App\bin\Debug\net8.0-windows" "%~dp0src\FlowUltraTech.App\bin\Debug\net8.0-windows\FlowUltraTech.App.exe"
)
echo.
echo Application started successfully! Check your screen and Windows taskbar.
timeout /t 3 >nul
