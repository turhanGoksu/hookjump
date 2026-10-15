"""Local SQLite storage for what hookjump learns (spec/STORAGE.md).

Raw samples are kept for 7 days; the seeks found in them and the listener's
pins are kept for good. The kept tables hold no dates or times.
"""

import sqlite3
from pathlib import Path

from hookjump.core.heatmap import RECENT_LISTENS
from hookjump.core.models import Pin, PlaybackSample, SeekEvent
from hookjump.core.seek import detect

SCHEMA_VERSION = 1
SAMPLE_DAYS = 7
_DAY_MS = 24 * 60 * 60 * 1000

_SCHEMA = """
CREATE TABLE pins (
    track_id TEXT PRIMARY KEY,
    at_ms INTEGER NOT NULL CHECK (at_ms >= 0),
    typed INTEGER NOT NULL CHECK (typed IN (0, 1))
);
CREATE TABLE seeks (
    track_id TEXT NOT NULL,
    listen INTEGER NOT NULL,
    from_ms INTEGER NOT NULL,
    to_ms INTEGER NOT NULL
);
CREATE INDEX seeks_by_track ON seeks (track_id, listen);
CREATE TABLE samples (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    at_ms INTEGER NOT NULL,
    track_id TEXT,
    playing INTEGER NOT NULL,
    position_ms INTEGER,
    own_seek INTEGER NOT NULL
);
CREATE TABLE ignored (
    track_id TEXT PRIMARY KEY
);
CREATE TABLE settings (
    key TEXT PRIMARY KEY,
    value INTEGER NOT NULL
);
"""


def default_path() -> Path:
    return Path.home() / "Library" / "Application Support" / "hookjump" / "hookjump.db"


class Store:
    """One hookjump database. Use as a context manager, or call close()."""

    def __init__(self, path: Path | str) -> None:
        if path != ":memory:":
            Path(path).parent.mkdir(parents=True, exist_ok=True)
        self._db = sqlite3.connect(path)
        # Deleted rows are overwritten, not left readable in free pages.
        self._db.execute("PRAGMA secure_delete = ON")
        version = self._db.execute("PRAGMA user_version").fetchone()[0]
        if version == 0:
            with self._db:
                self._db.executescript(_SCHEMA)
                self._db.execute(f"PRAGMA user_version = {SCHEMA_VERSION}")
        elif version != SCHEMA_VERSION:
            self._db.close()
            raise RuntimeError(
                f"{path} has schema version {version}; this hookjump reads"
                f" version {SCHEMA_VERSION}"
            )

    def close(self) -> None:
        self._db.close()

    def __enter__(self) -> "Store":
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()

    # Samples and seeks ------------------------------------------------------

    def add_sample(self, sample: PlaybackSample) -> int | None:
        """Record one poll; its ``at_ms`` is Unix time in milliseconds.

        Nothing is written while learning is paused or for an ignored track.
        Returns the sample's id, or None if it was not written.
        """
        if self.learning_paused() or (
            sample.track_id is not None and self.is_ignored(sample.track_id)
        ):
            return None
        with self._db:
            cursor = self._db.execute(
                "INSERT INTO samples (at_ms, track_id, playing, position_ms,"
                " own_seek) VALUES (?, ?, ?, ?, ?)",
                (
                    sample.at_ms,
                    sample.track_id,
                    int(sample.playing),
                    sample.position_ms,
                    int(sample.own_seek),
                ),
            )
        return cursor.lastrowid

    def process(self, before_id: int | None = None) -> int:
        """Find the seeks in the samples not processed yet and keep them.

        Pass the id of the first sample of the listen still going on, so a
        listen is never cut in two (it would count as two listens); pass None
        when nothing is playing any more, such as at start-up. Returns the
        number of seeks kept.
        """
        done = self._setting("processed_sample_id")
        rows = self._db.execute(
            "SELECT id, at_ms, track_id, playing, position_ms, own_seek"
            " FROM samples WHERE id > ? AND (? IS NULL OR id < ?) ORDER BY id",
            (done, before_id, before_id),
        ).fetchall()
        if not rows:
            return 0
        samples = [
            PlaybackSample(at, track, bool(playing), position, bool(own))
            for _, at, track, playing, position, own in rows
        ]
        seeks = [e for e in detect(samples) if isinstance(e, SeekEvent)]

        # detect() numbers listens within this batch. Kept listen numbers count
        # per track, only for listens with seeks, and only ever grow. Each seek
        # is inserted at once, so the next listen of the track counts on from it.
        numbers: dict[tuple[str, int], int] = {}
        with self._db:
            for seek in seeks:
                key = (seek.track_id, seek.listen)
                if key not in numbers:
                    numbers[key] = self._last_listen(seek.track_id) + 1
                self._db.execute(
                    "INSERT INTO seeks (track_id, listen, from_ms, to_ms)"
                    " VALUES (?, ?, ?, ?)",
                    (seek.track_id, numbers[key], seek.from_ms, seek.to_ms),
                )
            for track_id in {s.track_id for s in seeks}:
                self._keep_recent(track_id)
            self._set_setting("processed_sample_id", rows[-1][0])
        return len(seeks)

    def seeks(self, track_id: str) -> list[SeekEvent]:
        rows = self._db.execute(
            "SELECT listen, from_ms, to_ms FROM seeks WHERE track_id = ?"
            " ORDER BY listen, rowid",
            (track_id,),
        ).fetchall()
        return [SeekEvent(track_id, *row) for row in rows]

    def purge_samples(self, now_ms: int) -> int:
        """Delete processed samples older than SAMPLE_DAYS; returns how many."""
        with self._db:
            cursor = self._db.execute(
                "DELETE FROM samples WHERE at_ms < ? AND id <= ?",
                (now_ms - SAMPLE_DAYS * _DAY_MS, self._setting("processed_sample_id")),
            )
        return cursor.rowcount

    # Pins -------------------------------------------------------------------

    def pin(self, track_id: str) -> Pin | None:
        row = self._db.execute(
            "SELECT at_ms, typed FROM pins WHERE track_id = ?", (track_id,)
        ).fetchone()
        return Pin(row[0], bool(row[1])) if row else None

    def set_pin(self, track_id: str, pin: Pin) -> None:
        with self._db:
            self._db.execute(
                "INSERT OR REPLACE INTO pins (track_id, at_ms, typed) VALUES (?, ?, ?)",
                (track_id, pin.at_ms, int(pin.typed)),
            )

    def clear_pin(self, track_id: str) -> None:
        with self._db:
            self._db.execute("DELETE FROM pins WHERE track_id = ?", (track_id,))

    # Privacy ----------------------------------------------------------------

    def learning_paused(self) -> bool:
        return bool(self._setting("learning_paused"))

    def set_learning_paused(self, paused: bool) -> None:
        with self._db:
            self._set_setting("learning_paused", int(paused))

    def is_ignored(self, track_id: str) -> bool:
        row = self._db.execute(
            "SELECT 1 FROM ignored WHERE track_id = ?", (track_id,)
        ).fetchone()
        return row is not None

    def ignore(self, track_id: str) -> None:
        """Stop learning from a track and forget what was learned. Pins stay."""
        with self._db:
            self._db.execute(
                "INSERT OR IGNORE INTO ignored (track_id) VALUES (?)", (track_id,)
            )
            self._forget_learned(track_id)

    def unignore(self, track_id: str) -> None:
        with self._db:
            self._db.execute("DELETE FROM ignored WHERE track_id = ?", (track_id,))

    def forget_track(self, track_id: str) -> None:
        """Forget one track: its pin and everything learned from it."""
        with self._db:
            self._db.execute("DELETE FROM pins WHERE track_id = ?", (track_id,))
            self._forget_learned(track_id)

    def forget_learned(self) -> None:
        """Forget everything learned from listening; pins and settings stay."""
        with self._db:
            self._db.execute("DELETE FROM seeks")
            self._db.execute("DELETE FROM samples")

    def forget_all(self) -> None:
        """Delete everything: pins, learned data, ignored tracks, settings."""
        with self._db:
            for table in ("pins", "seeks", "samples", "ignored", "settings"):
                self._db.execute(f"DELETE FROM {table}")
        self._db.execute("VACUUM")  # give the space back to the disk

    # Internals --------------------------------------------------------------

    def _last_listen(self, track_id: str) -> int:
        row = self._db.execute(
            "SELECT MAX(listen) FROM seeks WHERE track_id = ?", (track_id,)
        ).fetchone()
        return row[0] or 0

    def _keep_recent(self, track_id: str) -> None:
        # The heatmap only reads the RECENT_LISTENS latest listens with seeks.
        self._db.execute(
            "DELETE FROM seeks WHERE track_id = ? AND listen NOT IN ("
            " SELECT DISTINCT listen FROM seeks WHERE track_id = ?"
            " ORDER BY listen DESC LIMIT ?)",
            (track_id, track_id, RECENT_LISTENS),
        )

    def _forget_learned(self, track_id: str) -> None:
        self._db.execute("DELETE FROM seeks WHERE track_id = ?", (track_id,))
        self._db.execute("DELETE FROM samples WHERE track_id = ?", (track_id,))

    def _setting(self, key: str) -> int:
        row = self._db.execute(
            "SELECT value FROM settings WHERE key = ?", (key,)
        ).fetchone()
        return row[0] if row else 0

    def _set_setting(self, key: str, value: int) -> None:
        self._db.execute(
            "INSERT OR REPLACE INTO settings (key, value) VALUES (?, ?)", (key, value)
        )
