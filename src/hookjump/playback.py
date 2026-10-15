"""The ear and the needle: the shared parts on top of any turntable.

The ear samples the player about once a second for learning. The needle jumps:
it places the playhead, checks where it landed, tries once more if it slipped,
and tells the ear, so that hookjump's own jumps never count as the listener's.
"""

import time
from collections.abc import Callable
from enum import Enum

from hookjump.adapters.turntable import Reading, Turntable
from hookjump.core.models import PlaybackSample
from hookjump.core.seek import TOLERANCE_MS


def unix_ms() -> int:
    return int(time.time() * 1000)


class Ear:
    """Samples a turntable: one reading becomes one PlaybackSample."""

    def __init__(self, turntable: Turntable, clock: Callable[[], int] = unix_ms):
        self._turntable = turntable
        self._clock = clock
        self._own_move = False

    def heard_own_move(self) -> None:
        """hookjump moved the playhead: mark the next sample as its own."""
        self._own_move = True

    def listen(self) -> tuple[Reading | None, PlaybackSample]:
        reading = self._turntable.read()
        at_ms = self._clock()
        own, self._own_move = self._own_move, False
        if reading is None:  # the player app is not running
            return None, PlaybackSample(at_ms, None, False, None, own)
        sample = PlaybackSample(
            at_ms, reading.track_id, reading.playing, reading.position_ms, own
        )
        return reading, sample


class Landing(Enum):
    """Where a jump ended up."""

    LANDED = "landed"  # the playhead is where it was sent
    TRACK_CHANGED = "track_changed"  # another track is playing now; no move
    SLIPPED = "slipped"  # the player took the move but is not there, twice
    NO_PLAYER = "no_player"  # the player app is not running


class Needle:
    """Jumps on a turntable and checks every jump."""

    RETRIES = 1  # one more try before reporting a slip

    def __init__(self, turntable: Turntable, ear: Ear) -> None:
        self._turntable = turntable
        self._ear = ear

    def drop(self, track_id: str, position_ms: int) -> Landing:
        for _ in range(1 + self.RETRIES):
            placed = self._turntable.place(track_id, position_ms)
            if placed:
                # Even a slipped move may have moved the playhead somewhere.
                self._ear.heard_own_move()
            reading = self._turntable.read()
            if reading is None:
                return Landing.NO_PLAYER
            if reading.track_id != track_id:
                return Landing.TRACK_CHANGED
            if placed and _near(reading.position_ms, position_ms):
                return Landing.LANDED
        return Landing.SLIPPED


def _near(position_ms: int | None, target_ms: int) -> bool:
    return position_ms is not None and abs(position_ms - target_ms) <= TOLERANCE_MS
