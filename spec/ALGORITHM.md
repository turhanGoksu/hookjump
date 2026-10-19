# hookjump core algorithm

This file is the source of truth for the core. The Python code in
`src/hookjump/core/` implements it; a port follows this file and must pass
`spec/vectors/`. Sections are added as each part is designed.

## 1. Units

Every time in the core is an **integer number of milliseconds**: playback
positions, durations and jump targets.

Integers compare exactly and print the same in every language, so an expected
value in a vector is never "close enough". Platform adapters convert at the
boundary, before anything reaches the core:

| Source | Native unit | Conversion to ms |
|---|---|---|
| Spotify on macOS, `player position` | float seconds | `floor(seconds * 1000 + 0.5)` |
| Spotify on macOS, track `duration` | integer ms (its dictionary says seconds; it is ms) | none |

The rounding formula is spelled out because languages round halves differently
(Python's `round` rounds half to even, Kotlin's `Math.round` rounds half up).
Positions are never negative, so `floor(x + 0.5)` gives one answer everywhere.

## 2. Statuses

Every jump decision carries exactly one status. Only `pinned` and `personal`
carry a target. The others carry none, and the app tells the listener why
instead of jumping somewhere random.

| Status | Target | Meaning |
|---|---|---|
| `pinned` | yes | a spot the listener chose by hand for this track |
| `personal` | yes | from the listener's own heatmap |
| `no_data` | no | nothing pinned, and not enough listening yet |
| `player_unavailable` | no | Spotify is not running or has no track loaded |

A target is never negative.

When a track has both, the pin wins: a choice made by hand beats one learned
from listening.

There is no other source. hookjump does not guess from lyrics, from the audio
or from other people's listening, and makes no network request: a track with
nothing pinned or learned gets `no_data`.

## 3. Seek detection

Input: samples polled from the player about once a second, in time order. A
sample has `at_ms` (a clock in milliseconds; only differences matter),
`track_id` (or null), `playing`, `position_ms` (or null) and `own_seek`
(hookjump itself moved the playhead since the previous sample). Stored samples
use Unix time, so they stay comparable across restarts; a step of the system
clock can at worst cause one wrong event. Output: events in order, each
either a **play** span or a **seek**, with `track_id`, `listen`, `from_ms` and
`to_ms`. Vectors: `vectors/seek_detect.json`.

A **listen** is one play-through of a track. Listens are numbered from 1 within
one input.

Constants: `TOLERANCE = 2000` (polling jitter), `MAX_GAP = 5000`,
`RESTART = 3000`.

1. Skip every sample whose `track_id` or `position_ms` is null.
2. The first sample, and every sample with a different `track_id` than the
   previous one, starts a new listen. Nothing else is inferred from it.
3. Otherwise compare the sample (`b`) with the previous one (`a`):
   `elapsed = b.at_ms - a.at_ms`, `moved = b.position_ms - a.position_ms`.
4. If `b.own_seek`, or `elapsed <= 0`, or `elapsed > MAX_GAP`: no event. A jump
   made by hookjump must never count as the listener's own move; otherwise a
   learned favorite would reinforce itself.
5. The expected range of `moved`:
   - `low = (elapsed if a.playing and b.playing else 0) - TOLERANCE`
   - `high = (elapsed if a.playing or b.playing else 0) + TOLERANCE`

   Playing throughout, the position moves by about `elapsed`; paused or resumed
   in between, by anything from 0 to `elapsed`; paused throughout, not at all.
6. If `low <= moved <= high`: playback. When `moved > 0`, add a play span
   `a.position_ms` to `b.position_ms`. If the previous event is a play span of
   the same track and listen that ends exactly at `a.position_ms`, extend it
   instead (playback carrying on after a pause).
7. Else, if `moved < 0` and `b.position_ms < RESTART`: the track was started
   over (the previous button, or repeat). Start a new listen; no seek.
8. Else: a seek from `a.position_ms` to `b.position_ms`. Both ends are known
   only to within one sample interval: the playhead moved somewhere between
   the two samples.

## 4. The listener's favorite part

Input: the seeks of **one** track (section 3) on record. Listen numbers tell
different listens of the track apart and grow with time: a later listen has a
higher number. Output: the favorite part (`target_ms`, `points`, `listens`),
or none yet. Vectors: `vectors/heatmap_favorite.json`.

Untouched playback says nothing about taste, so only the listener's own moves
count. Seconds heard twice in one listen are not counted either: the only way
to hear a part again is to rewind to it, and the rewind is already counted.

Constants: `REWIND_POINTS = 3`, `FORWARD_POINTS = 2`, `REGION_GAP = 5000`,
`MIN_POINTS = 5`, `MIN_LISTENS = 2`, `RECENT_LISTENS = 10`.

0. **Recent listens only.** Keep the seeks of the `RECENT_LISTENS` highest
   listen numbers among the seeks; drop the rest. Only listens that contain a
   seek count, so listens without one (untouched, or jumped by hookjump) never
   push old seeks out. Otherwise a learned favorite would fade exactly because
   the hotkey now does the seeking. New seeks do push old ones out, so a change
   of taste is followed: by listening, not by the calendar.
1. A seek is a **rewind** when `to_ms < from_ms`, otherwise a **forward skip**.
   Only its arrival point `to_ms` matters.
2. Sort the seeks by `to_ms` and group them into **regions**: walking in
   order, a seek whose `to_ms` is more than `REGION_GAP` after the previous
   one's starts a new region. Exactly `REGION_GAP` apart is the same region.
3. For each region: `points` = `REWIND_POINTS` per rewind plus
   `FORWARD_POINTS` per forward skip; `listens` = the number of different
   listen numbers among its seeks.
4. A region qualifies when `points >= MIN_POINTS` and `listens >= MIN_LISTENS`:
   one stray seek, or many seeks in a single sitting, is not yet a habit. For
   example two rewinds in two listens qualify (6 points), and so do a rewind
   and a forward skip (5); two forward skips do not (4).
5. Among qualifying regions, the one with the most points wins; on equal
   points, the one with more listens; on a full tie, the earliest.
6. `target_ms` is the lower median of the winning region's arrival points: with
   `n` points in time order, the one at index `(n - 1) // 2` (counting from 0).
   It is always a point the listener actually chose.
7. No qualifying region: no favorite yet.

## 5. Choosing the jump

Input: whether a track is loaded; the track's pin (`at_ms`, `typed`) or none;
and its favorite part (section 4) or none. Output: a status and a target.
Vectors: `vectors/target_choose.json`.

Constant: `LEAD = 1500`. Targets are measured a little late: the pin hotkey is
pressed after the part has started, and a seek's arrival is read at the next
poll. Landing `LEAD` earlier keeps the first word. `lead(t) = max(0, t - LEAD)`.

Take the first rule that applies:

1. No track loaded: `player_unavailable`.
2. A pin: `pinned`. A typed pin is exact, `target = at_ms`: the listener said
   where. A hotkey pin lands at `lead(at_ms)`.
3. A favorite part: `personal`, `target = lead(target_ms)`.
4. Otherwise: `no_data`.
