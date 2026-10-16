"""Stand-ins for the player and the clock, shared by the tests."""

from hookjump.adapters.turntable import Reading


class FakeTurntable:
    """A player in memory. ``ignores`` moves are accepted but not done."""

    name = "Fake"

    def __init__(self, track: str | None = "a", position_ms: int = 30000) -> None:
        self.running = True
        self.track = track
        self.position_ms: int | None = position_ms
        self.ignores = 0  # how many coming moves to accept but not do
        self.lands_off_by = 0
        self.switch_to: str | None = None  # the listener changes track first
        self.places = 0

    def read(self) -> Reading | None:
        if not self.running:
            return None
        return Reading(self.track, "Title", "Artist", 240000, True, self.position_ms)

    def place(self, track_id: str, position_ms: int) -> bool:
        self.places += 1
        if self.switch_to:
            self.track, self.position_ms = self.switch_to, 0
        if not self.running or self.track != track_id:
            return False
        if self.ignores:
            self.ignores -= 1
        else:
            self.position_ms = position_ms + self.lands_off_by
        return True


class ManualClock:
    """Stands still until a test moves it."""

    def __init__(self, now: int = 1_700_000_000_000) -> None:
        self.now = now

    def __call__(self) -> int:
        return self.now


class Clock:
    def __init__(self) -> None:
        self.now = 1_700_000_000_000

    def __call__(self) -> int:
        self.now += 1000
        return self.now
