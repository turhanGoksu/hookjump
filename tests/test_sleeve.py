"""The record sleeve, as data: what goes where on the strip."""

import pytest

from hookjump.adapters.turntable import Reading
from hookjump.core.models import Favorite, Pin, SeekEvent
from hookjump.sleeve import fraction, ms_at, sleeve_for

READING = Reading("a", "Alışamadım", "Ezhel", 153268, True, 40000)
SEEKS = [SeekEvent("a", 1, 130000, 105000), SeekEvent("a", 2, 10000, 60000)]


def test_a_pin_is_the_hook() -> None:
    sleeve = sleeve_for(READING, Pin(72000, typed=False), None, SEEKS)
    assert sleeve is not None
    assert (sleeve.title, sleeve.now_ms, sleeve.hook_ms) == (
        "Alışamadım — Ezhel",
        40000,
        72000,
    )
    assert sleeve.caption() == "◆ 1:12 pinned"
    assert (sleeve.rewinds, sleeve.skips) == ((105000,), (60000,))


def test_a_typed_pin_says_so() -> None:
    sleeve = sleeve_for(READING, Pin(83000, typed=True), None, [])
    assert sleeve is not None and sleeve.caption() == "◆ 1:23 typed"


def test_a_learned_favorite_says_how_it_was_learned() -> None:
    sleeve = sleeve_for(READING, None, Favorite(105000, 6, 2), SEEKS)
    assert sleeve is not None and sleeve.caption() == "◆ 1:45 learned · 2 listens"


def test_nothing_known_invites_a_click() -> None:
    sleeve = sleeve_for(READING, None, None, [])
    assert sleeve is not None and sleeve.hook_ms is None
    assert sleeve.caption() == "nothing learned yet · click the line to pin"


@pytest.mark.parametrize(
    "reading",
    [
        None,
        Reading(None, None, None, None, False, None),
        Reading("a", "T", "A", None, True, 1000),  # no length: nothing to draw
    ],
)
def test_no_sleeve_without_a_track_to_draw(reading: Reading | None) -> None:
    assert sleeve_for(reading, None, None, []) is None


def test_a_missing_title_or_artist() -> None:
    reading = Reading("a", None, "Ezhel", 153268, True, 0)
    assert sleeve_for(reading, None, None, []).title == "Ezhel"
    nameless = Reading("a", None, None, 153268, True, 0)
    assert sleeve_for(nameless, None, None, []).title == "Unknown track"


def test_fraction_and_back() -> None:
    assert fraction(76634, 153268) == 0.5
    assert fraction(-5, 153268) == 0.0 and fraction(999999, 153268) == 1.0
    assert ms_at(0.5, 153268) == 77000  # whole seconds
    assert ms_at(1.2, 153268) == 153000  # the end, to the whole second
    assert ms_at(1.0, 153600) == 153600  # rounding up never passes the end
    assert ms_at(-0.1, 153268) == 0
