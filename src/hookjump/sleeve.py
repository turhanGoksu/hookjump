"""The record sleeve: the strip at the top of the menu, as plain data.

It shows the playing track as a line, with what hookjump learned on it: where
the listener rewound to (│), where they skipped to (╵), the hook (◆) and where
playback is now (●). A click on the strip pins that spot. Drawing it is in
hookjump.sleeve_view; this module only decides what goes where.
"""

from dataclasses import dataclass

from hookjump.adapters.turntable import Reading
from hookjump.core.models import Favorite, Pin, SeekEvent
from hookjump.words import clock


@dataclass(frozen=True)
class Sleeve:
    title: str
    duration_ms: int
    now_ms: int | None
    hook_ms: int | None  # where the hook is, before any lead-in
    hook_note: str  # what the hook is, for the caption
    rewinds: tuple[int, ...]  # where the listener rewound to
    skips: tuple[int, ...]  # where they skipped forward to

    def caption(self) -> str:
        if self.hook_ms is None:
            return "nothing learned yet · click the line to pin"
        return f"◆ {clock(self.hook_ms)} {self.hook_note}"


def sleeve_for(
    reading: Reading | None,
    pin: Pin | None,
    favorite: Favorite | None,
    seeks: list[SeekEvent],
) -> Sleeve | None:
    """None when there is no track with a known length to draw."""
    if reading is None or reading.track_id is None or not reading.duration_ms:
        return None
    if pin is not None:
        hook_ms, note = pin.at_ms, "typed" if pin.typed else "pinned"
    elif favorite is not None:
        hook_ms, note = favorite.target_ms, f"learned · {favorite.listens} listens"
    else:
        hook_ms, note = None, ""
    title = " — ".join(part for part in (reading.title, reading.artist) if part)
    return Sleeve(
        title=title or "Unknown track",
        duration_ms=reading.duration_ms,
        now_ms=reading.position_ms,
        hook_ms=hook_ms,
        hook_note=note,
        rewinds=tuple(s.to_ms for s in seeks if s.to_ms < s.from_ms),
        skips=tuple(s.to_ms for s in seeks if s.to_ms >= s.from_ms),
    )


def fraction(ms: int, duration_ms: int) -> float:
    """Where ``ms`` sits along the line, from 0.0 to 1.0."""
    return min(1.0, max(0.0, ms / duration_ms))


def ms_at(fraction_: float, duration_ms: int) -> int:
    """The moment under a point of the line, to the whole second."""
    ms = round(min(1.0, max(0.0, fraction_)) * duration_ms / 1000) * 1000
    return min(ms, duration_ms)
