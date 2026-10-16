"""What hookjump says in the menu and on the command line."""

import pytest

from hookjump.words import clock, describe_jump, describe_pin, parse_clock


@pytest.mark.parametrize(
    ("ms", "shown"), [(0, "0:00"), (103500, "1:43"), (59999, "0:59"), (600000, "10:00")]
)
def test_clock(ms: int, shown: str) -> None:
    assert clock(ms) == shown


def test_a_landed_jump() -> None:
    text = describe_jump("personal", 103500, "landed")
    assert text == "Jumped to 1:43: the part you replay most."


def test_a_slipped_jump_says_what_happened() -> None:
    text = describe_jump("pinned", 72000, "slipped")
    assert text == "Tried 1:12 (your pinned spot), but Spotify did not move there."


@pytest.mark.parametrize(
    ("status", "reason"),
    [
        ("no_data", "nothing pinned or learned"),
        ("player_unavailable", "Spotify is not playing"),
        ("not_found", "lyrics were not found"),
    ],
)
def test_no_jump_says_why(status: str, reason: str) -> None:
    text = describe_jump(status, None, None)
    assert text.startswith("No jump: ") and reason in text


def test_pins() -> None:
    assert describe_pin(72000) == "Pinned 1:12. The jump lands 1.5 s before it."
    assert describe_pin(83000, exact=True) == "Pinned 1:23. The jump lands right there."
    assert describe_pin(None).startswith("Nothing pinned")


@pytest.mark.parametrize(
    ("text", "ms"),
    [("1:23", 83000), (" 1:23 ", 83000), ("1:23.5", 83500), ("0:07", 7000),
     ("83", 83000), ("12:00", 720000)],
)  # fmt: skip
def test_parse_clock(text: str, ms: int) -> None:
    assert parse_clock(text) == ms


@pytest.mark.parametrize("text", ["", "abc", "1:60", "1:-5", "-1:20", "1:2:3", "nan"])
def test_parse_clock_refuses(text: str) -> None:
    assert parse_clock(text) is None
