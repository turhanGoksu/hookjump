"""The booth, end to end on a fake turntable and a real store."""

from pathlib import Path

import pytest
from fakes import FakeTurntable, ManualClock

from hookjump.adapters.storage import Store
from hookjump.booth import PURGE_EVERY_MS, Booth
from hookjump.core.models import Pin, PlaybackSample, Status
from hookjump.face import Mood
from hookjump.playback import Landing

REWIND = [130000, 131000, 105000, 106000]  # one rewind to 1:45


class Rig:
    def __init__(self, tmp_path: Path) -> None:
        self.store = Store(tmp_path / "hookjump.db")
        self.player = FakeTurntable("a", 30000)
        self.clock = ManualClock()
        self.booth = Booth(self.store, self.player, self.clock)

    def play(self, track: str | None, positions: list[int]) -> None:
        """The listener plays ``track`` through these positions, one per second."""
        self.player.track = track
        for position in positions:
            self.player.position_ms = position
            self.booth.tick()
            self.clock.now += 1000


@pytest.fixture
def rig(tmp_path: Path) -> Rig:
    rig = Rig(tmp_path)
    yield rig
    rig.store.close()


def test_a_rewind_is_learned_when_the_track_changes(rig: Rig) -> None:
    rig.play("a", REWIND)
    assert rig.store.seeks("a") == []  # the listen is still going on
    rig.play("b", [1000])
    assert [(s.from_ms, s.to_ms) for s in rig.store.seeks("a")] == [(131000, 105000)]


def test_a_skip_right_after_a_track_starts_is_not_lost(rig: Rig) -> None:
    # Skipping the intro is common; the first two samples of a listen belong
    # together, so the listen must not be cut after its first sample.
    rig.play("a", [30000])
    rig.play("b", [1000, 60000, 61000])
    rig.play("a", [31000])
    assert [(s.from_ms, s.to_ms) for s in rig.store.seeks("b")] == [(1000, 60000)]


def test_nothing_known_is_no_data_and_nothing_moves(rig: Rig) -> None:
    rig.play("a", [30000])
    jumped = rig.booth.jump()
    assert jumped.result.status is Status.NO_DATA and jumped.landing is None
    assert rig.player.places == 0
    assert jumped.frames[0].text.endswith("✕")


def test_two_listens_teach_the_favorite(rig: Rig) -> None:
    for _ in range(2):
        rig.play("a", REWIND)
        rig.play("b", [1000])
    rig.play("a", [30000])
    jumped = rig.booth.jump()
    assert (jumped.result.status, jumped.result.target_ms) == (Status.PERSONAL, 103500)
    assert jumped.landing is Landing.LANDED and rig.player.position_ms == 103500


def test_a_hotkey_pin_wins_and_its_jump_is_never_learned(rig: Rig) -> None:
    rig.play("a", [106000, 106200])
    pinned = rig.booth.pin()
    assert pinned.pin == Pin(106200, typed=False)
    assert rig.store.pin("a") == pinned.pin
    rig.play("b", [1000])  # another song, then track a again from the start
    rig.play("a", [30000, 31000])
    jumped = rig.booth.jump()
    assert (jumped.result.status, jumped.result.target_ms) == (Status.PINNED, 104700)
    rig.play("a", [104700, 105700, 106700])  # playback goes on from the jump
    rig.play("b", [1000])
    # The pin was set with a hotkey, and the jump back was hookjump's own: the
    # heatmap learned no seek from either.
    assert rig.store.seeks("a") == []


def test_a_typed_pin_is_exact(rig: Rig) -> None:
    rig.play("a", [30000])
    assert rig.booth.pin_at(83000).pin == Pin(83000, typed=True)
    assert rig.booth.jump().result.target_ms == 83000


def test_with_spotify_closed_nothing_happens_and_it_says_so(rig: Rig) -> None:
    rig.player.running = False
    jumped = rig.booth.jump()
    assert jumped.result.status is Status.PLAYER_UNAVAILABLE
    assert jumped.frames[0].text == "✕"
    pinned = rig.booth.pin()
    assert pinned.pin is None and pinned.frames[0].text == "✕"


def test_the_moods(rig: Rig) -> None:
    rig.play(None, [0])
    assert rig.booth.mood() is Mood.RESTING  # nothing playing
    rig.play("a", [30000])
    assert rig.booth.mood() is Mood.UNKNOWN
    rig.booth.pin()
    assert rig.booth.mood() is Mood.KNOWN
    rig.store.set_learning_paused(True)
    assert rig.booth.mood() is Mood.RESTING


def test_leftovers_of_the_last_run_are_learned_at_start(tmp_path: Path) -> None:
    store = Store(tmp_path / "hookjump.db")
    for i, position in enumerate(REWIND):
        store.add_sample(
            PlaybackSample(1_700_000_000_000 + i * 1000, "a", True, position)
        )
    Booth(store, FakeTurntable(), ManualClock())
    assert len(store.seeks("a")) == 1
    store.close()


def test_old_samples_are_purged_once_an_hour(rig: Rig) -> None:
    rig.clock.now -= 9 * 24 * PURGE_EVERY_MS  # nine days ago
    rig.play("a", REWIND)
    rig.play("b", [1000])  # processed now, still too young to purge
    before = count_samples(rig.store)
    rig.clock.now += 9 * 24 * PURGE_EVERY_MS  # back to today
    rig.play("b", [2000])
    assert count_samples(rig.store) < before


def count_samples(store: Store) -> int:
    return store._db.execute("SELECT COUNT(*) FROM samples").fetchone()[0]
