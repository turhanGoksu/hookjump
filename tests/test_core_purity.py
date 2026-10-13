"""The core stays portable: it imports only pure-computation standard modules.

No os, sys, time, sqlite3, urllib, subprocess or PyObjC: the core receives data
as arguments and returns data, so it can be re-implemented on any platform and
tested without a player, a network or a clock.
"""

import ast
from collections.abc import Iterator
from pathlib import Path

import hookjump.core

CORE_DIR = Path(hookjump.core.__file__).parent
ALLOWED = frozenset(
    {
        "__future__",
        "bisect",
        "collections",
        "dataclasses",
        "enum",
        "math",
        "re",
        "typing",
        "unicodedata",
    }
)


def imported_modules(path: Path) -> Iterator[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            yield from (alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.level == 0 and node.module:
            yield node.module  # relative imports stay inside the core


def is_allowed(module: str) -> bool:
    return module.split(".")[0] in ALLOWED or module.startswith("hookjump.core")


def test_core_imports_only_pure_modules() -> None:
    files = sorted(CORE_DIR.rglob("*.py"))
    assert files, f"no core modules found in {CORE_DIR}"
    bad = [
        (str(f.relative_to(CORE_DIR)), m)
        for f in files
        for m in imported_modules(f)
        if not is_allowed(m)
    ]
    assert not bad, f"core imports non-pure modules: {bad}"


def test_purity_check_catches_platform_imports(tmp_path: Path) -> None:
    # Guard the guard: a check that never fails proves nothing.
    sneaky = tmp_path / "sneaky.py"
    sneaky.write_text(
        "import subprocess\nfrom Foundation import NSAppleScript\n"
        "from hookjump.adapters import spotify\nfrom .models import Status\n",
        encoding="utf-8",
    )
    found = list(imported_modules(sneaky))
    assert [m for m in found if not is_allowed(m)] == [
        "subprocess",
        "Foundation",
        "hookjump.adapters",
    ]
