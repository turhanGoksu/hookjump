"""LRC parsing, checked against spec/vectors/lrc_parse.json."""

import pytest
from spec_vectors import cases

from hookjump.core.lrc import parse_lrc


@pytest.mark.parametrize("case", cases("lrc_parse.json"))
def test_parse_lrc(case: dict) -> None:
    parsed = parse_lrc("\n".join(case["lrc"]))
    lines = [{"start_ms": ln.start_ms, "text": ln.text} for ln in parsed.lines]
    assert lines == case["expected"]["lines"]
    assert parsed.skipped == case["expected"]["skipped"]


def test_start_times_are_int_milliseconds() -> None:
    parsed = parse_lrc("[01:02.5]la")
    assert type(parsed.lines[0].start_ms) is int
