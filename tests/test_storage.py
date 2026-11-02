"""Local storage: pins, learned seeks, the 7-day samples and forgetting."""

import sqlite3
from pathlib import Path

import pytest

from hookjump.adapters import storage as storage_module
from hookjump.adapters.storage import SAMPLE_DAYS, Kept, Store, default_path
from hookjump.core.heatmap import RECENT_LISTENS, favorite
from hookjump.core.models import Pin, PlaybackSample, Status
from hookjump.core.target import choose

DAY_MS = 24 * 60 * 60 * 1000
REWIND = [130000, 131000, 105000, 106000]  # one rewind, 131000 -> 105000


@pytest.fixture
def store(tmp_path: Path) -> Store:
    with Store(tmp_path / "hookjump.db") as s:
        yield s


class Clock:
    """Feeds samples one second apart, as the adapter would poll."""

    def __init__(self, store: Store, start_ms: int = 1_700_000_000_000) -> None:
        self.store, self.at = store, start_ms

    def play(self, track: str, positions: list[int]) -> list[int | None]:
        ids = []
        for position in positions:
            ids.append(
                self.store.add_sample(PlaybackSample(self.at, track, True, position))
            )
            self.at += 1000
        return ids


def count(store: Store, table: str) -> int:
    return store._db.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]


def test_pins_round_trip(store: Store) -> None:
    assert store.pin("a") is None
    store.set_pin("a", Pin(83000, typed=True))
    assert store.pin("a") == Pin(83000, True)
    store.set_pin("a", Pin(106200, typed=False))  # a new pin replaces the old
    assert store.pin("a") == Pin(106200, False)
    store.clear_pin("a")
    assert store.pin("a") is None


def test_listen_numbers_count_per_track(store: Store) -> None:
    clock = Clock(store)
    clock.play("a", REWIND)
    clock.play("b", REWIND)
    clock.play("a", REWIND)
    assert store.process() == 3
    assert [s.listen for s in store.seeks("a")] == [1, 2]
    assert [s.listen for s in store.seeks("b")] == [1]
    assert (store.seeks("a")[0].from_ms, store.seeks("a")[0].to_ms) == (131000, 105000)


def test_listen_numbers_continue_across_batches(store: Store) -> None:
    clock = Clock(store)
    clock.play("a", REWIND)
    store.process()
    clock.play("b", [5000, 6000])
    clock.play("a", REWIND)
    store.process()
    assert [s.listen for s in store.seeks("a")] == [1, 2]


def test_the_listen_still_going_on_waits(store: Store) -> None:
    clock = Clock(store)
    clock.play("a", REWIND)
    first_of_b = clock.play("b", REWIND[:2])[0]
    assert store.process(before_id=first_of_b) == 1  # only track a's listen
    clock.play("b", REWIND[2:])  # the same listen of b goes on, then ends
    assert store.process() == 1
    assert [s.listen for s in store.seeks("b")] == [1]  # one listen, not two


def test_only_the_recent_listens_with_seeks_are_kept(store: Store) -> None:
    clock = Clock(store)
    for _ in range(RECENT_LISTENS + 2):
        clock.play("a", REWIND)
        clock.play("b", [5000, 6000])  # a change of track ends each listen of a
    store.process()
    assert [s.listen for s in store.seeks("a")] == list(range(3, RECENT_LISTENS + 3))


def test_paused_learning_writes_nothing(store: Store) -> None:
    store.set_learning_paused(True)
    assert Clock(store).play("a", REWIND) == [None] * 4
    assert count(store, "samples") == 0
    store.set_learning_paused(False)
    assert None not in Clock(store).play("a", REWIND)


def test_an_ignored_track_is_forgotten_and_not_learned_but_keeps_its_pin(
    store: Store,
) -> None:
    clock = Clock(store)
    clock.play("a", REWIND)
    store.process()
    store.set_pin("a", Pin(50000, typed=True))
    store.ignore("a")
    assert store.is_ignored("a")
    assert store.seeks("a") == []
    assert store.pin("a") == Pin(50000, True)
    assert clock.play("a", REWIND) == [None] * 4
    assert None not in clock.play("b", REWIND)  # other tracks are still learned
    store.unignore("a")
    assert None not in clock.play("a", REWIND)


def test_forget_track_touches_only_that_track(store: Store) -> None:
    clock = Clock(store)
    clock.play("a", REWIND)
    clock.play("b", REWIND)
    store.process()
    store.set_pin("a", Pin(1000, False))
    store.set_pin("b", Pin(2000, False))
    store.forget_track("a")
    assert (store.pin("a"), store.seeks("a")) == (None, [])
    assert store.pin("b") == Pin(2000, False)
    assert len(store.seeks("b")) == 1
    rows = store._db.execute("SELECT DISTINCT track_id FROM samples").fetchall()
    assert rows == [("b",)]


def test_forget_learned_keeps_pins_ignored_tracks_and_settings(store: Store) -> None:
    Clock(store).play("a", REWIND)
    store.process()
    store.set_pin("a", Pin(1000, True))
    store.ignore("c")
    store.set_learning_paused(True)
    store.forget_learned()
    assert (count(store, "seeks"), count(store, "samples")) == (0, 0)
    assert store.pin("a") == Pin(1000, True)
    assert store.is_ignored("c") and store.learning_paused()


def test_forget_all_leaves_an_empty_working_store(store: Store) -> None:
    clock = Clock(store)
    clock.play("a", REWIND)
    store.process()
    store.set_pin("a", Pin(1000, True))
    store.ignore("c")
    store.set_learning_paused(True)
    store.forget_all()
    for table in ("pins", "seeks", "samples", "ignored", "settings", "tracks"):
        assert count(store, table) == 0, table
    assert not store.learning_paused()
    clock.play("a", REWIND)
    assert store.process() == 1  # the processed mark started over with it


def test_sample_ids_are_never_reused(store: Store) -> None:
    # Reused ids would sit below the processed mark and be skipped for good.
    clock = Clock(store)
    old = clock.play("a", REWIND)
    store.process()
    store.forget_learned()
    new = clock.play("a", REWIND)
    assert min(new) > max(old)
    assert store.process() == 1


def test_purge_drops_only_old_processed_samples(store: Store) -> None:
    now = 1_700_000_000_000
    old = Clock(store, now - (SAMPLE_DAYS + 1) * DAY_MS)
    old.play("a", REWIND)
    store.process()
    old.play("b", REWIND)  # old but not processed yet: must survive
    Clock(store, now - DAY_MS).play("c", REWIND)  # recent
    assert store.purge_samples(now) == 4
    rows = store._db.execute("SELECT DISTINCT track_id FROM samples").fetchall()
    assert sorted(rows) == [("b",), ("c",)]


def test_data_survives_reopening(tmp_path: Path) -> None:
    path = tmp_path / "sub" / "hookjump.db"  # the folder is created
    with Store(path) as store:
        store.set_pin("a", Pin(1000, True))
    with Store(path) as store:
        assert store.pin("a") == Pin(1000, True)


def test_a_newer_schema_is_refused(tmp_path: Path) -> None:
    path = tmp_path / "hookjump.db"
    Store(path).close()
    db = sqlite3.connect(path)
    db.execute("PRAGMA user_version = 3")
    db.close()
    with pytest.raises(RuntimeError, match="schema version 3"):
        Store(path)


def test_deleted_data_is_overwritten(store: Store) -> None:
    assert store._db.execute("PRAGMA secure_delete").fetchone()[0] == 1


def test_default_path_is_in_application_support() -> None:
    assert default_path().parts[-4:] == (
        "Library",
        "Application Support",
        "hookjump",
        "hookjump.db",
    )


def test_from_polls_to_a_jump(store: Store) -> None:
    # Two listens rewinding to 1:45: the next jump lands 1.5 s before it.
    clock = Clock(store)
    for _ in range(2):
        clock.play("a", REWIND)
        clock.play("b", [5000, 6000])
    store.process()
    found = favorite(store.seeks("a"))
    result = choose(track_loaded=True, pin=None, favorite=found)
    assert (result.status, result.target_ms) == (Status.PERSONAL, 103500)


def test_shortcuts_are_kept_and_reset_with_everything(store: Store) -> None:
    assert store.shortcut("jump") == 0  # 0: the default
    store.set_shortcut("jump", 0x26 << 16 | 0x1100)
    assert store.shortcut("jump") == 0x26 << 16 | 0x1100
    assert store.shortcut("pin") == 0
    store.forget_all()
    assert store.shortcut("jump") == 0


def names(store: Store) -> list[tuple[str, str | None, str | None]]:
    return store._db.execute("SELECT * FROM tracks ORDER BY track_id").fetchall()


def test_a_song_only_listened_to_is_never_named(store: Store) -> None:
    Clock(store).play("a", [5000, 6000, 7000])
    store.process()
    assert store.name("a", "Song", "Artist") is False
    assert names(store) == []


def test_a_name_lives_as_long_as_something_else_is_kept(store: Store) -> None:
    store.set_pin("a", Pin(1000, False))
    assert store.name("a", "Song", "Artist")
    assert names(store) == [("a", "Song", "Artist")]
    store.name("a", "Song", "Artist (live)")  # a changed name is written
    assert names(store) == [("a", "Song", "Artist (live)")]
    store.clear_pin("a")
    assert names(store) == []


def test_names_follow_seeks_ignoring_and_forgetting(store: Store) -> None:
    clock = Clock(store)
    clock.play("a", REWIND)
    clock.play("b", REWIND)
    store.process()
    assert store.name("a", "A", None) and store.name("b", "B", None)
    store.ignore("a")  # seeks go, the ignored row keeps the name
    assert [n[0] for n in names(store)] == ["a", "b"]
    store.unignore("a")
    assert [n[0] for n in names(store)] == ["b"]
    store.forget_learned()
    assert names(store) == []


def test_forgetting_a_song_forgets_its_name_unless_it_is_ignored(store: Store) -> None:
    store.set_pin("a", Pin(1000, False))
    store.set_pin("b", Pin(1000, False))
    store.ignore("b")
    store.name("a", "A", None)
    store.name("b", "B", None)
    store.forget_track("a")
    store.forget_track("b")
    assert [n[0] for n in names(store)] == ["b"]  # still not tracked, so still shown


def test_what_is_kept_is_listed_named_first(store: Store) -> None:
    clock = Clock(store)
    clock.play("z", REWIND)
    store.process()
    store.set_pin("a", Pin(83000, True))
    store.ignore("m")
    store.name("a", "beta", "X")
    store.name("m", "Alpha", None)
    kept = store.kept()
    assert [k.track_id for k in kept] == ["m", "a", "z"]  # by title, unnamed last
    assert kept[0] == Kept("m", "Alpha", None, None, True)
    assert kept[1].pin == Pin(83000, True) and not kept[1].ignored
    assert kept[2].title is None


def test_a_version_1_file_is_upgraded_in_place(tmp_path: Path) -> None:
    path = tmp_path / "hookjump.db"
    db = sqlite3.connect(path)
    db.executescript(storage_module._SCHEMA)
    db.execute("INSERT INTO pins VALUES ('a', 1000, 1)")
    db.execute("PRAGMA user_version = 1")
    db.commit()
    db.close()
    with Store(path) as store:
        assert store.pin("a") == Pin(1000, True)
        assert store.name("a", "Song", None)
        assert store._db.execute("PRAGMA user_version").fetchone()[0] == 2
