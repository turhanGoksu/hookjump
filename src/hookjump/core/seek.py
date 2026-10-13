"""Turn polled player positions into listening events (spec/ALGORITHM.md, section 5).

The adapter asks the player for its position about once a second. Between two
samples of the same track, the position should advance by about the time that
passed; anything far from that is the listener moving the playhead.
"""

from collections.abc import Sequence
from dataclasses import replace

from hookjump.core.models import PlaybackSample, PlaySpan, SeekEvent

TOLERANCE_MS = 2000  # polling jitter; smaller moves are not seeks
MAX_GAP_MS = 5000  # a longer gap between samples (sleep, a hang) says nothing
RESTART_MS = 3000  # going back to before this is starting the track over


def detect(samples: Sequence[PlaybackSample]) -> list[PlaySpan | SeekEvent]:
    """Listening events, in order, from samples in time order."""
    events: list[PlaySpan | SeekEvent] = []
    listen = 0
    prev: PlaybackSample | None = None
    for sample in samples:
        if sample.track_id is None or sample.position_ms is None:
            continue
        if prev is None or sample.track_id != prev.track_id:
            listen += 1
            prev = sample
            continue

        elapsed = sample.at_ms - prev.at_ms
        moved = sample.position_ms - prev.position_ms
        if sample.own_seek or not 0 < elapsed <= MAX_GAP_MS:
            prev = sample
            continue

        # Playing during the whole interval: the position moves by `elapsed`.
        # Playing during part of it (paused or resumed in between): by anything
        # from 0 to `elapsed`. Paused throughout: not at all.
        both = prev.playing and sample.playing
        either = prev.playing or sample.playing
        low = (elapsed if both else 0) - TOLERANCE_MS
        high = (elapsed if either else 0) + TOLERANCE_MS
        if low <= moved <= high:
            if moved > 0:
                _add_play(events, sample.track_id, listen, prev.position_ms, moved)
        elif moved < 0 and sample.position_ms < RESTART_MS:
            listen += 1
        else:
            events.append(
                SeekEvent(sample.track_id, listen, prev.position_ms, sample.position_ms)
            )
        prev = sample
    return events


def _add_play(
    events: list[PlaySpan | SeekEvent],
    track_id: str,
    listen: int,
    from_ms: int,
    moved: int,
) -> None:
    # Extend the last span when this one carries straight on from it, as after
    # a pause; otherwise start a new span.
    last = events[-1] if events else None
    if isinstance(last, PlaySpan) and last.to_ms == from_ms:
        if (last.track_id, last.listen) == (track_id, listen):
            events[-1] = replace(last, to_ms=from_ms + moved)
            return
    events.append(PlaySpan(track_id, listen, from_ms, from_ms + moved))
