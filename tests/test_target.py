"""Choosing the jump, checked against spec/vectors/target_choose.json."""

import pytest
from spec_vectors import cases

from hookjump.core.models import Favorite, JumpResult, Pin, Status
from hookjump.core.target import choose


@pytest.mark.parametrize("case", cases("target_choose.json"))
def test_choose(case: dict) -> None:
    pin = Pin(**case["pin"]) if case["pin"] else None
    favorite = Favorite(**case["favorite"]) if case["favorite"] else None
    chorus = case["chorus"]
    if chorus:
        chorus = JumpResult(Status(chorus["status"]), chorus["target_ms"])
    result = choose(
        track_loaded=case["track_loaded"], pin=pin, favorite=favorite, chorus=chorus
    )
    assert {"status": result.status.value, "target_ms": result.target_ms} == case[
        "expected"
    ]


@pytest.mark.parametrize("status", [Status.PINNED, Status.PERSONAL, Status.NO_DATA])
def test_chorus_input_must_be_a_chorus_guess(status: Status) -> None:
    guess = JumpResult(status, 1000 if status.has_target else None)
    with pytest.raises(ValueError, match="not a chorus guess"):
        choose(track_loaded=True, pin=None, favorite=None, chorus=guess)
