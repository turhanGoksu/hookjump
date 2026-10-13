"""Load the language-neutral test vectors in spec/vectors/."""

import json
from pathlib import Path
from typing import Any

import pytest

VECTORS_DIR = Path(__file__).resolve().parent.parent / "spec" / "vectors"


def cases(filename: str) -> list[Any]:
    """The cases of one vector file, as pytest params named after each case."""
    data = json.loads((VECTORS_DIR / filename).read_text(encoding="utf-8"))
    names = [case["name"] for case in data["cases"]]
    assert len(names) == len(set(names)), f"duplicate case names in {filename}"
    return [pytest.param(case, id=case["name"]) for case in data["cases"]]
