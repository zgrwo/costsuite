#!/bin/bash
# ──────────────────────────────────────────────────────────
# test-quality-guard.sh — 弱断言检测 + 测试覆盖统计（bash 版）
# 用于 CI 和 pre-commit（无 PowerShell 依赖）
# ──────────────────────────────────────────────────────────
set -e
REPO_ROOT=$(cd "$(dirname "$0")/.." && pwd)

echo ""
echo "🔍 Test Quality Guard (bash)"
echo "═══════════════════════════════"

# ── 1. 弱断言检测 ──
echo ""
echo "[1/3] 弱断言检测..."

WEAK_COUNT=0
for f in $(find "$REPO_ROOT/tests" -name "*Tests.cs" -type f); do
    fname=$(basename "$f")
    # 检测仅 NotNull 作为唯一断言的 [Fact] 方法
    # 简化：统计弱断言模式
    WEAK=$(grep -c '\.Should()\.NotBeNull()\s*;' "$f" 2>/dev/null || true)
    GREATER=$(grep -c '\.Should()\.BeGreaterThan(0)\s*;' "$f" 2>/dev/null || true)
    EMPTY=$(grep -c '\.Should()\.NotBeNullOrEmpty()\s*;' "$f" 2>/dev/null || true)
    if [ "$WEAK" -gt 0 ] || [ "$GREATER" -gt 0 ] || [ "$EMPTY" -gt 0 ]; then
        echo "  $fname: NotNull=$WEAK BeGreaterThan(0)=$GREATER NotNullOrEmpty=$EMPTY"
        WEAK_COUNT=$((WEAK_COUNT + WEAK + GREATER + EMPTY))
    fi
done
echo "  弱断言总数: $WEAK_COUNT"

# ── 2. 测试数量统计 ──
echo ""
echo "[2/3] 测试数量统计..."

UNIT_TESTS=$(find "$REPO_ROOT/tests/BomAddIn.UnitTests" -name "*.cs" -type f | xargs grep -c '\[Fact\]\|\[Theory\]' 2>/dev/null | awk -F: '{s+=$2} END {print s}')
INT_TESTS=$(find "$REPO_ROOT/tests/BomAddIn.IntegrationTests" -name "*.cs" -type f | xargs grep -c '\[Fact\]\|\[Theory\]' 2>/dev/null | awk -F: '{s+=$2} END {print s}')
THREAD_TESTS=$(find "$REPO_ROOT/tests/BomAddIn.ThreadingTests" -name "*.cs" -type f | xargs grep -c '\[Fact\]\|\[Theory\]' 2>/dev/null | awk -F: '{s+=$2} END {print s}')

echo "  单元测试: ${UNIT_TESTS:-0}"
echo "  集成测试: ${INT_TESTS:-0}"
echo "  线程测试: ${THREAD_TESTS:-0}"
echo "  总计: $((${UNIT_TESTS:-0} + ${INT_TESTS:-0} + ${THREAD_TESTS:-0}))"

# ── 3. 未测试 Repository 检测 ──
echo ""
echo "[3/3] 未测试文件检测..."

UNTESTED=""
for repo in $(find "$REPO_ROOT/src/BomAddIn.Data/Repositories" -name "*.cs" -type f | grep -v '^I'); do
    base=$(basename "$repo" .cs)
    testfile="$REPO_ROOT/tests/BomAddIn.IntegrationTests/${base}Tests.cs"
    if [ ! -f "$testfile" ]; then
        UNTESTED="$UNTESTED $base"
    fi
done

if [ -n "$UNTESTED" ]; then
    echo "  ⚠ 未测试 Repository:$UNTESTED"
else
    echo "  ✅ 所有 Repository 有对应测试文件"
fi

# ── 4. static 共享状态检查 ──
echo ""
STATIC_STATE=$(grep -rn 'private static int _counter\|private static long _counter' "$REPO_ROOT/tests/" 2>/dev/null || true)
if [ -n "$STATIC_STATE" ]; then
    echo "  ⚠ 发现 static 可变状态（并行测试不安全）:"
    echo "$STATIC_STATE"
fi
THREAD_SLEEP=$(grep -rn 'Thread\.Sleep(' "$REPO_ROOT/tests/" 2>/dev/null || true)
if [ -n "$THREAD_SLEEP" ]; then
    echo "  ⚠ 发现 Thread.Sleep（CI 中可能不稳定）:"
    echo "$THREAD_SLEEP"
fi

echo ""
echo "✅ 测试质量检查完成。"
