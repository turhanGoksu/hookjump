"""The ear and the needle, on a fake turntable that can misbehave."""

from hookjump.adapters.turntable import Reading
from hookjump.core.models import SeekEvent
from hookjump.core.seek import detect
from hookjump.playback import Ear, Landing, Needle


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


class Clock:
    def __init__(self) -> None:
        self.now = 1_700_000_000_000

    def __call__(self) -> int:
        self.now += 1000
        return self.now


def rig(turntable: FakeTurntable) -> tuple[Ear, Needle]:
    ear = Ear(turntable, Clock())
    return ear, Needle(turntable, ear)


def test_the_ear_turns_a_reading_into_a_sample() -> None:
    ear, _ = rig(FakeTurntable("a", 45000))
    reading, sample = ear.listen()
    assert reading is not None and reading.title == "Title"
    assert (sample.track_id, sample.playing, sample.position_ms) == ("a", True, 45000)
    assert sample.at_ms == 1_700_000_001_000 and not sample.own_seek


def test_the_ear_hears_nothing_when_the_player_is_closed() -> None:
    turntable = FakeTurntable()
    turntable.running = False
    reading, sample = rig(turntable)[0].listen()
    assert reading is None
    assert (sample.track_id, sample.position_ms, sample.playing) == (None, None, False)


def test_a_jump_lands_and_marks_only_the_next_sample() -> None:
    turntable = FakeTurntable()
    ear, needle = rig(turntable)
    assert needle.drop("a", 103500) == Landing.LANDED
    assert turntable.places == 1
    assert ear.listen()[1].own_seek
    assert not ear.listen()[1].own_seek


def test_a_slip_is_tried_once_more() -> None:
    turntable = FakeTurntable()
    turntable.ignores = 1
    assert rig(turntable)[1].drop("a", 103500) == Landing.LANDED
    assert turntable.places == 2


def test_a_player_that_ignores_moves_is_reported_after_two_tries() -> None:
    # Spotify on Windows was reported to accept a seek and do nothing.
    turntable = FakeTurntable()
    turntable.ignores = 99
    ear, needle = rig(turntable)
    assert needle.drop("a", 103500) == Landing.SLIPPED
    assert turntable.places == 2
    assert ear.listen()[1].own_seek  # it may still have moved somewhere


def test_landing_close_enough_counts() -> None:
    turntable = FakeTurntable()
    turntable.lands_off_by = 2000
    assert rig(turntable)[1].drop("a", 103500) == Landing.LANDED
    turntable.lands_off_by = 2001
    assert rig(turntable)[1].drop("a", 103500) == Landing.SLIPPED


def test_an_unknown_position_after_the_move_is_a_slip() -> None:
    turntable = FakeTurntable()
    turntable.lands_off_by = 0
    needle = rig(turntable)[1]
    turntable.read = lambda: Reading("a", None, None, None, True, None)  # type: ignore[method-assign]
    assert needle.drop("a", 103500) == Landing.SLIPPED


def test_a_new_track_is_never_moved() -> None:
    turntable = FakeTurntable()
    turntable.switch_to = "b"
    ear, needle = rig(turntable)
    assert needle.drop("a", 103500) == Landing.TRACK_CHANGED
    assert turntable.position_ms == 0  # track b was not touched
    assert not ear.listen()[1].own_seek


def test_a_closed_player_is_reported() -> None:
    turntable = FakeTurntable()
    turntable.running = False
    assert rig(turntable)[1].drop("a", 103500) == Landing.NO_PLAYER


def test_hookjumps_own_jump_never_becomes_a_seek() -> None:
    # Listen, jump with the needle, listen on: seek detection finds no seek.
    turntable = FakeTurntable("a", 30000)
    ear, needle = rig(turntable)
    samples = []
    for _ in range(3):
        samples.append(ear.listen()[1])
        turntable.position_ms += 1000
    needle.drop("a", 103500)
    for _ in range(3):
        samples.append(ear.listen()[1])
        turntable.position_ms += 1000
    assert not [e for e in detect(samples) if isinstance(e, SeekEvent)]
