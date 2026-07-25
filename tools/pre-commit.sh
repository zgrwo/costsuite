#!/bin/bash
# ──────────────────────────────────────────────────────────
# pre-commit hook — BomAddIn 测试质量 + 构建守卫
#
# 检查内容:
#   1. dotnet build（编译通过）
#   2. 测试质量守卫（弱断言检测、测试命名）
#   3. dotnet test（全部测试通过）
#
# 安装: cp tools/pre-commit.sh .git/hooks/pre-commit && chmod +x .git/hooks/pre-commit
# ──────────────────────────────────────────────────────────

set -e
REPO_ROOT=$(git rev-parse --show-toplevel)

echo "🔍 BomAddIn Pre-Commit Guard — 开始检查..."

# ═══ 1. 编译检查 ═══
echo ""
echo "[1/3] dotnet build..."
dotnet build "$REPO_ROOT/BomAddIn.sln" --nologo -v q
if [ $? -ne 0 ]; then
    echo "❌ 编译失败，提交中止。"
    exit 1
fi
echo "✅ 编译通过"

# ═══ 2. 测试质量守卫 ═══
echo ""
echo "[2/3] 测试质量检查..."
if command -v pwsh &> /dev/null; then
    pwsh -NoProfile -File "$REPO_ROOT/tools/test-quality-guard.ps1" -Mode Quick
    GUARD_EXIT=$?
    if [ $GUARD_EXIT -eq 2 ]; then
        echo "❌ 测试质量问题（弱断言等），建议修复后再提交。"
        echo "   使用 --no-verify 跳过（不推荐）。"
        exit 1
    fi
else
    # pwsh 不可用时使用 bash 版质量守卫
    bash "$REPO_ROOT/tools/test-quality-guard.sh"
fi
echo "✅ 测试质量通过"

# ═══ 3. 全部测试 ═══
echo ""
echo "[3/3] dotnet test..."
dotnet test "$REPO_ROOT/BomAddIn.sln" --nologo --no-build -v q
if [ $? -ne 0 ]; then
    echo "❌ 测试失败，提交中止。"
    exit 1
fi
echo "✅ 全部测试通过"

echo ""
echo "✅✅✅ 所有检查通过，允许提交。"
exit 0
