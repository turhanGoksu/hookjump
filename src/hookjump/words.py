"""What hookjump says, in words: for the menu and for the command line."""

from __future__ import annotations

from typing import TYPE_CHECKING

from hookjump.core.models import Status

if TYPE_CHECKING:  # the commands use these words too, without the booth
    from hookjump.booth import Record

_REASONS = {
    Status.PINNED: "your pinned spot",
    Status.PERSONAL: "the part you replay most",
    Status.NO_DATA: "nothing pinned or learned for this song yet",
    Status.PLAYER_UNAVAILABLE: "Spotify is not playing anything",
}
_LANDINGS = {
    "slipped": "Spotify did not move there",
    "track_changed": "the song changed meanwhile",
    "no_player": "Spotify closed meanwhile",
}


def clock(ms: int) -> str:
    """1:43 for 103500; seconds are cut, not rounded, like players show them."""
    return f"{ms // 60_000}:{ms % 60_000 // 1000:02d}"


def describe_jump(status: str, target_ms: int | None, landing: str | None) -> str:
    reason = _REASONS[Status(status)]
    if target_ms is None:
        return f"No jump: {reason}."
    if landing and landing != "landed":
        return f"Tried {clock(target_ms)} ({reason}), but {_LANDINGS[landing]}."
    return f"Jumped to {clock(target_ms)}: {reason}."


def describe_pin(pinned_ms: int | None, exact: bool = False) -> str:
    if pinned_ms is None:
        return "Nothing pinned: Spotify is not playing anything."
    if exact:
        return f"Pinned {clock(pinned_ms)}. The jump lands right there."
    return f"Pinned {clock(pinned_ms)}. The jump lands 1.5 s before it."


# "What hookjump Keeps": the songs something is kept about.

NOTHING_KEPT = (
    "Nothing kept yet.",
    "Pin a spot, or rewind to a part you love in two different listens.",
)
NOTHING_LEAVES = "Nothing leaves this Mac."


def record_name(record: Record) -> str | None:
    """``Ou — UZI, Motive``; None when the song has no name kept."""
    return " — ".join(part for part in (record.title, record.artist) if part) or None


def describe_record(record: Record) -> list[str]:
    """What is kept about one song, one fact per line."""
    lines = []
    if record.pin is not None:
        typed = ", typed" if record.pin.typed else ""
        lines.append(f"Pinned at {clock(record.pin.at_ms)}{typed}")
    if record.favorite is not None:
        listens = _count(record.favorite.listens, "listen")
        lines.append(f"Learned {clock(record.favorite.target_ms)}, from {listens}")
    elif record.seeks:
        seeks, listens = _count(record.seeks, "seek"), _count(record.listens, "listen")
        lines.append(f"{seeks} in {listens}, nothing learned yet")
    if record.ignored:
        lines.append("Not tracking")
    return lines


def unnamed_songs(count: int) -> str:
    """The songs kept about before names were, which have not played since."""
    return f"{_count(count, 'song')} not played since names were kept"


def shorten(text: str, limit: int = 48) -> str:
    """At most ``limit`` characters, ending in … when cut."""
    return text if len(text) <= limit else text[: limit - 1].rstrip() + "…"


def _count(n: int, word: str) -> str:
    return f"1 {word}" if n == 1 else f"{n} {word}s"


def parse_clock(text: str) -> int | None:
    """``1:23`` or ``1:23.5`` or ``83`` to milliseconds; None if it is not a time."""
    text = text.strip()
    minutes, _, seconds = text.rpartition(":")
    try:
        whole = int(minutes) if minutes else 0
        part = float(seconds)
    except ValueError:
        return None
    if whole < 0 or part < 0 or (minutes and part >= 60) or part != part:  # NaN
        return None
    return whole * 60_000 + round(part * 1000)
