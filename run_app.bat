@echo off
title TikTok Studio Sandbox Launcher
echo ===================================================
echo   TikTok LIVE Studio Sandbox Manager Launcher
echo ===================================================
echo.
echo Stopping any existing instances...
taskkill /F /IM TiktokStudioSandbox.App.exe 2>nul
echo.
if exist "%~dp0dist\publish\TiktokStudioSandbox.App.exe" (
    echo Launching Published Release version...
    start "" /D "%~dp0dist\publish" "%~dp0dist\publish\TiktokStudioSandbox.App.exe"
) else (
    echo Launching Debug version...
    start "" /D "%~dp0src\TiktokStudioSandbox.App\bin\Debug\net8.0-windows" "%~dp0src\TiktokStudioSandbox.App\bin\Debug\net8.0-windows\TiktokStudioSandbox.App.exe"
)
echo.
echo Application started successfully! Check your screen and Windows taskbar.
timeout /t 3 >nul
