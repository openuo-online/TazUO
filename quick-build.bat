@echo off
chcp 65001 >nul

echo ========================================
echo    快速编译 TazUO (跳过清理)
echo ========================================
echo.

REM 关闭运行中的游戏
tasklist /FI "IMAGENAME eq TazUO.exe" 2>NUL | find /I /N "TazUO.exe">NUL
if "%ERRORLEVEL%"=="0" (
    taskkill /F /IM TazUO.exe >NUL 2>&1
    timeout /t 1 /nobreak >NUL
)

echo 快速增量编译中...
dotnet build src\ClassicUO.Client\ClassicUO.Client.csproj --configuration Debug --no-restore --no-dependencies --verbosity quiet

if %ERRORLEVEL% EQU 0 (
    echo ✓ 编译成功！启动游戏...
    cd bin\Debug\net9.0\win-x64
    start TazUO.exe
    timeout /t 2 /nobreak >NUL
) else (
    echo ✗ 编译失败！
    pause
)
