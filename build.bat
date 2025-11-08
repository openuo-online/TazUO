@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ========================================
echo    清理并编译 TazUO
echo ========================================
echo.

echo [0/3] 检查并关闭运行中的游戏...
tasklist /FI "IMAGENAME eq TazUO.exe" 2>NUL | find /I /N "TazUO.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo 发现运行中的 TazUO.exe，正在关闭...
    taskkill /F /IM TazUO.exe >NUL 2>&1
    timeout /t 2 /nobreak >NUL
    echo 已关闭游戏进程
) else (
    echo 没有运行中的游戏进程
)

echo.
echo [1/3] 清理项目...
dotnet clean --configuration Debug

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [错误] 清理失败！
    pause
    exit /b 1
)

echo.
echo [2/3] 编译项目...
dotnet build --configuration Debug --verbosity minimal

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo    ✓ 编译成功！
    echo ========================================
    echo.
) else (
    echo.
    echo [错误] 编译失败！
    echo.
)

pause
