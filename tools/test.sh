#!/usr/bin/env bash
# 墓时 · 自动化测试入口（一条命令跑全部机器可测的功能）
#   用法：bash tools/test.sh
#   退出码 0 = 全过，1 = 有失败。
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-/mnt/c/Program Files/dotnet/dotnet.exe}"
GODOT="${GODOT:-/mnt/c/Users/wzt17/App/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe}"
PROJ="$(wslpath -w "$ROOT" 2>/dev/null || echo "$ROOT")"

echo "== 1/3 编译 =="
"$DOTNET" build "$ROOT/mobius-meovverse.csproj" -v quiet --nologo 2>&1 | grep -E "error CS" && { echo "编译失败"; exit 1; }
echo "   ok"

echo "== 2/3 集成测试（68+ 断言：章节路由 / 擦描玩法 / 对话布局 / 音频闸门 / HUD）=="
"$GODOT" --headless --path "$PROJ" res://scenes/IntegrationTest.tscn 2>&1 \
  | sed 's/\x1b\[[0-9;]*m//g' | grep -E "^  ✗|结果"
RES="${PIPESTATUS[0]}"

echo "== 3/3 墓园自动走链（RPGExplo 自动驾驶到 A 区 7 号）=="
"$GODOT" --headless --path "$PROJ" res://scenes/RPGExplo.tscn -- auto 2>&1 \
  | sed 's/\x1b\[[0-9;]*m//g' | grep -E "走到了|ERROR" | head -3

echo "== 完成（集成测试退出码 $RES：0=全过）=="
exit "$RES"
