"""Choosing the jump, checked against spec/vectors/target_choose.json."""

import pytest
from spec_vectors import cases

from hookjump.core.models import Favorite, Pin
from hookjump.core.target import choose


@pytest.mark.parametrize("case", cases("target_choose.json"))
def test_choose(case: dict) -> None:
    pin = Pin(**case["pin"]) if case["pin"] else None
    favorite = Favorite(**case["favorite"]) if case["favorite"] else None
    result = choose(track_loaded=case["track_loaded"], pin=pin, favorite=favorite)
    assert {"status": result.status.value, "target_ms": result.target_ms} == case[
        "expected"
    ]
