# hookjump storage format

Everything hookjump learns stays in one SQLite file on the listener's device.
On macOS: `~/Library/Application Support/hookjump/hookjump.db`; other platforms
use their app's private data folder. The Python implementation is
`src/hookjump/adapters/storage.py`; this file is enough to re-implement it.

## Versioning and deletion

- `PRAGMA user_version` is `1`. A reader refuses a file with a higher version
  rather than guess at it.
- Every connection sets `PRAGMA secure_delete = ON`, so deleted rows are
  overwritten instead of staying readable in free pages of the file.

## Tables

```sql
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
```

Times are integer milliseconds and booleans are `0` or `1`, as in
`ALGORITHM.md`. `track_id` is the player's id, such as a Spotify URI.

| Table | Holds | Kept |
|---|---|---|
| `pins` | the spot the listener chose for a track; `typed = 1` if typed as m:ss (exact), `0` if set with the hotkey or a click on the menu's line (approximate) | until removed |
| `seeks` | the listener's seeks (`ALGORITHM.md` section 5) | per track, the `RECENT_LISTENS` latest listens with seeks |
| `samples` | raw polls; `at_ms` is Unix time | 7 days once processed |
| `ignored` | tracks the listener asked not to learn from | until removed |
| `settings` | `processed_sample_id`, `learning_paused` (`0`/`1`); a missing key reads as `0` | until removed |

## Rules

1. **Recording.** A poll is written to `samples` unless learning is paused or
   its track is in `ignored`. Sample ids come from `AUTOINCREMENT` and are
   never reused, so a new sample can never fall below the processed mark.
2. **Processing.** Take the samples after `processed_sample_id`, in id order,
   up to but not including the first sample of the listen still going on (all
   of them when nothing is playing, such as at start-up). A listen is never cut
   in two: halves would count as two listens. Run seek detection on them. For
   each seek, its listen gets a per-track number: the largest `listen` of that
   track in `seeks` plus one, given in order of appearance; seeks of the same
   detected listen share it. Then, for each track touched, delete its seeks
   outside the `RECENT_LISTENS` highest listen numbers (`ALGORITHM.md`
   section 6). Move `processed_sample_id` to the last sample taken. All of
   this happens in one transaction.
3. **Retention.** Delete samples older than 7 days whose id is at or below
   `processed_sample_id`. Unprocessed samples are never deleted.
4. **Ignoring a track** adds it to `ignored` and deletes its seeks and
   samples. Its pin stays: a pin is a choice, not tracking.
5. **Forgetting.**
   - One track: its pin, seeks and samples.
   - Everything learned: all seeks and samples; pins, `ignored` and settings
     stay.
   - Everything: every table emptied, then `VACUUM` to give the space back.

## Privacy

- `pins`, `seeks`, `ignored` and `settings` hold no dates or times. Listen
  numbers count per track, so they do not even tell in which order different
  tracks were heard.
- Only `samples` carries times, and only for 7 days.
- Nothing in this file leaves the device.
