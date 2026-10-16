"""The booth: where the turntable, the ear, the needle and the records meet.

Both doors (the hotkeys and the ``hookjump`` command) and the menu end here,
so a jump or a pin goes through the same few lines however it was asked for.
No UI and no player calls of its own: in tests it runs on a fake turntable.
"""

from collections.abc import Callable
from dataclasses import dataclass

from hookjump.adapters.storage import Store
from hookjump.adapters.turntable import Reading, Turntable
from hookjump.core.heatmap import favorite
from hookjump.core.models import JumpResult, Pin, Status
from hookjump.core.target import choose
from hookjump.face import Frame, Mood, jump_frames, mood, pin_frames
from hookjump.playback import Ear, Landing, Needle, unix_ms

PURGE_EVERY_MS = 60 * 60 * 1000


@dataclass(frozen=True)
class Jumped:
    result: JumpResult
    landing: Landing | None  # None when nothing was moved
    frames: list[Frame]


@dataclass(frozen=True)
class Pinned:
    pin: Pin | None  # None: nothing is playing, so nothing was pinned
    frames: list[Frame]


class Booth:
    def __init__(
        self, store: Store, turntable: Turntable, clock: Callable[[], int] = unix_ms
    ) -> None:
        self._store = store
        self._turntable = turntable
        self._clock = clock
        self._ear = Ear(turntable, clock)
        self._needle = Needle(turntable, self._ear)
        self._track: str | None = None  # the track of the listen going on
        self._purged_at = 0
        self.reading: Reading | None = None  # the latest, for the menu
        # Samples left from the last run belong to listens that are over.
        store.process()

    def tick(self) -> None:
        """Listen once; call about every second while the app runs."""
        self.reading, sample = self._ear.listen()
        sample_id = self._store.add_sample(sample)
        if sample.track_id != self._track:
            # A new track: the listen before it is over, so learn from it. The
            # new listen starts at this sample and waits for its own end.
            self._store.process(before_id=sample_id)
            self._track = sample.track_id
        now = self._clock()
        if now - self._purged_at >= PURGE_EVERY_MS:
            self._store.purge_samples(now)
            self._purged_at = now

    def jump(self) -> Jumped:
        reading = self._turntable.read()
        if reading is None or reading.track_id is None:
            result = JumpResult(Status.PLAYER_UNAVAILABLE)
            return Jumped(result, None, jump_frames(None, None, result, None))
        track = reading.track_id
        result = choose(
            track_loaded=True,
            pin=self._store.pin(track),
            favorite=favorite(self._store.seeks(track)),
            chorus=None,  # the chorus guess is an opt-in extra, still to come
        )
        landing = None
        if result.target_ms is not None:
            landing = self._needle.drop(track, result.target_ms)
        frames = jump_frames(reading.position_ms, reading.duration_ms, result, landing)
        return Jumped(result, landing, frames)

    def pin(self) -> Pinned:
        """Pin where the playing track is now (the pin hotkey)."""
        reading = self._turntable.read()
        if reading is None or reading.track_id is None or reading.position_ms is None:
            nothing = JumpResult(Status.PLAYER_UNAVAILABLE)
            return Pinned(None, jump_frames(None, None, nothing, None))
        return self._pin(reading, Pin(reading.position_ms, typed=False))

    def pin_at(self, position_ms: int) -> Pinned:
        """Pin a time the listener typed, such as 1:23: exact, no lead-in."""
        reading = self._turntable.read()
        if reading is None or reading.track_id is None:
            nothing = JumpResult(Status.PLAYER_UNAVAILABLE)
            return Pinned(None, jump_frames(None, None, nothing, None))
        return self._pin(reading, Pin(position_ms, typed=True))

    def mood(self) -> Mood:
        track = self.reading.track_id if self.reading else None
        known = track is not None and (
            self._store.pin(track) is not None
            or favorite(self._store.seeks(track)) is not None
        )
        return mood(
            playing_track=track is not None,
            learning_paused=self._store.learning_paused(),
            spot_known=known,
        )

    def _pin(self, reading: Reading, pin: Pin) -> Pinned:
        assert reading.track_id is not None
        old = self._store.pin(reading.track_id)
        self._store.set_pin(reading.track_id, pin)
        frames = pin_frames(
            reading.position_ms, reading.duration_ms, old.at_ms if old else None
        )
        return Pinned(pin, frames)
