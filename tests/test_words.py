"""What hookjump says in the menu and on the command line."""

import pytest

from hookjump.booth import Record
from hookjump.core.models import Favorite, Pin
from hookjump.words import (
    clock,
    describe_jump,
    describe_pin,
    describe_record,
    parse_clock,
    record_name,
    shorten,
    unnamed_songs,
)


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


def record(**changes) -> Record:
    fields = dict(
        title="Ou", artist="UZI, Motive", pin=None, favorite=None,
        seeks=0, listens=0, ignored=False,
    )  # fmt: skip
    return Record(**(fields | changes))


def test_a_record_is_named_by_title_and_artist() -> None:
    assert record_name(record()) == "Ou — UZI, Motive"
    assert record_name(record(artist=None)) == "Ou"
    assert record_name(record(title=None, artist=None)) is None


def test_a_record_says_what_is_kept() -> None:
    both = record(pin=Pin(106000, typed=False), favorite=Favorite(105230, 8, 2))
    assert describe_record(both) == ["Pinned at 1:46", "Learned 1:45, from 2 listens"]
    typed = record(pin=Pin(83000, typed=True), ignored=True)
    assert describe_record(typed) == ["Pinned at 1:23, typed", "Not tracking"]


def test_seeks_without_a_favorite_are_counted() -> None:
    assert describe_record(record(seeks=3, listens=3)) == [
        "3 seeks in 3 listens, nothing learned yet"
    ]
    assert describe_record(record(seeks=1, listens=1)) == [
        "1 seek in 1 listen, nothing learned yet"
    ]


def test_songs_without_a_name() -> None:
    assert unnamed_songs(1) == "1 song not played since names were kept"
    assert unnamed_songs(32) == "32 songs not played since names were kept"


def test_shorten() -> None:
    assert shorten("Ou — UZI, Motive") == "Ou — UZI, Motive"
    name = "KARANLIK — Yung Kafa & Kücük Efendi, Motive, Jumpa, Yung Kafa"
    short = shorten(name)
    assert len(short) == 48 and short.endswith("…") and name.startswith(short[:-1])
    assert shorten("abc d", 5) == "abc d" and shorten("abc def", 5) == "abc…"
