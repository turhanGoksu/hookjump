"""Seek detection, checked against spec/vectors/seek_detect.json."""

import pytest
from spec_vectors import cases

from hookjump.core.models import PlaybackSample, PlaySpan
from hookjump.core.seek import detect


def as_dict(event: object) -> dict:
    kind = "play" if isinstance(event, PlaySpan) else "seek"
    return {"type": kind, **vars(event)}


@pytest.mark.parametrize("case", cases("seek_detect.json"))
def test_detect(case: dict) -> None:
    samples = [PlaybackSample(**s) for s in case["samples"]]
    assert [as_dict(e) for e in detect(samples)] == case["expected"]
