"""Line comparison keys, checked against spec/vectors/line_key.json."""

import pytest
from spec_vectors import cases

from hookjump.core.normalize import line_key


@pytest.mark.parametrize("case", cases("line_key.json"))
def test_line_key(case: dict) -> None:
    assert line_key(case["text"]) == case["expected"]


def test_plain_lower_is_not_turkish_aware() -> None:
    # The trap line_key avoids: the dotted capital I gains a combining dot.
    assert "İki".lower() != "iki"
    assert line_key("İki") == "iki"


def test_same_line_written_differently_gets_one_key() -> None:
    assert line_key("GİDİYORUM!") == line_key("gidiyorum") == line_key("Gıdıyorum")
