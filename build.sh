#!/bin/bash

echo "========================================"
echo "   清理并编译 TazUO"
echo "========================================"
echo ""

echo "[0/3] 检查并关闭运行中的游戏..."
if pgrep -x "TazUO" > /dev/null; then
    echo "发现运行中的 TazUO，正在关闭..."
    pkill -9 TazUO
    sleep 2
    echo "已关闭游戏进程"
else
    echo "没有运行中的游戏进程"
fi

echo ""
echo "[1/3] 清理项目..."
dotnet clean --configuration Debug

if [ $? -ne 0 ]; then
    echo ""
    echo "[错误] 清理失败！"
    exit 1
fi

echo ""
echo "[2/3] 编译项目..."
dotnet build --configuration Debug --verbosity minimal

if [ $? -eq 0 ]; then
    echo ""
    echo "========================================"
    echo "   ✓ 编译成功！"
    echo "========================================"
    echo ""
else
    echo ""
    echo "[错误] 编译失败！"
    echo ""
    exit 1
fi
