#!/usr/bin/env python3
"""check-doc-links.py — Markdown 链接/反引号路径存在性检查

对齐模板 verify-docs.py 语义（2026-08 治理对齐的轻量移植）：
1. Markdown 链接 `[text](target)` — 相对文档目录解析；跳过外部链接/纯 #锚点
2. 反引号路径 `` `path/to/file` `` — 以已知根目录前缀开头时按仓库根解析；
   占位符/模式串（xxx/nnn/{{/{/}</>/*/...）与 AI 工具本地目录（.claude/.codegraph/.qoder）
   不校验；CHANGELOG.md 为历史记录不校验

断链/无效路径 → 打印并 exit 1（CI 门禁，防文档漂移）。
"""
from __future__ import annotations

import argparse
import contextlib
import re
import sys
from pathlib import Path

with contextlib.suppress(AttributeError, ValueError):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent

SKIP_DIRS = {".git", "node_modules", "__pycache__", ".venv", "build", "dist", "obj", "bin", "logs"}
KNOWN_ROOT_PREFIXES = (
    "scripts/", "rules/", "skills/", "templates/", ".github/",
    "docs/", "tests/", "src/", "tools/", "build/",
)
BACKTICK_SKIP_MARKERS = ("xxx", "nnn", "{{", "{", "}", "<", ">", "*", "...")
BACKTICK_SKIP_PREFIXES = (".claude/", ".codegraph/", ".qoder/")
SKIP_DOCS = {"CHANGELOG.md"}

MD_LINK_RE = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
BT_PATH_RE = re.compile(r"`([^`\s]+)`")


def _check_md_links(doc: Path, root: Path, problems: list[str]) -> None:
    try:
        text = doc.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return
    base = doc.parent
    for m in MD_LINK_RE.finditer(text):
        target = m.group(1).split("#", 1)[0].strip()
        if not target or target.startswith(("http://", "https://", "mailto:", "//", "tel:")):
            continue
        target = target.replace("%20", " ")
        if target.startswith("./"):
            target = target[2:]
        rel = doc.relative_to(root)
        if not (base / target).resolve().exists():
            problems.append(f"[断链] {rel}: 链接目标不存在 -> {m.group(1)}")


def _check_backtick_paths(doc: Path, root: Path, problems: list[str]) -> None:
    if doc.name in SKIP_DOCS:
        return
    try:
        text = doc.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return
    rel = doc.relative_to(root)
    for m in BT_PATH_RE.finditer(text):
        raw = m.group(1)
        target = raw.replace("\\", "/")
        if target.startswith("./"):
            target = target[2:]
        if not target.startswith(KNOWN_ROOT_PREFIXES):
            continue
        if target.startswith(BACKTICK_SKIP_PREFIXES):
            continue
        if any(mk in target for mk in BACKTICK_SKIP_MARKERS):
            continue
        if not (root / target.rstrip("/")).exists():
            problems.append(f"[无效路径] {rel}: 反引号路径不存在 -> {raw}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Markdown 链接/路径存在性检查")
    parser.add_argument("--root", default=str(ROOT), help="仓库根目录")
    args = parser.parse_args(argv)

    root = Path(args.root).resolve()
    problems: list[str] = []
    n_files = 0
    for p in sorted(root.rglob("*.md")):
        if any(part in SKIP_DIRS for part in p.relative_to(root).parts):
            continue
        n_files += 1
        _check_md_links(p, root, problems)
        _check_backtick_paths(p, root, problems)

    if problems:
        print(f"[FAIL] 发现 {len(problems)} 处文档引用问题：")
        for pr in problems:
            print(f"  - {pr}")
        return 1
    print(f"[OK] 文档链接一致性验证通过（{n_files} 个 .md 文件）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
