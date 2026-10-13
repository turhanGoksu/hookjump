# hookjump core algorithm

This file is the source of truth for the core. The Python code in
`src/hookjump/core/` implements it; a port follows this file and must pass
`spec/vectors/`. Sections are added as each part is designed.

## 1. Units

Every time in the core is an **integer number of milliseconds**: playback
positions, durations, lyric timestamps and jump targets.

Integers compare exactly and print the same in every language, so an expected
value in a vector is never "close enough". Platform adapters convert at the
boundary, before anything reaches the core:

| Source | Native unit | Conversion to ms |
|---|---|---|
| Spotify on macOS, `player position` | float seconds | `floor(seconds * 1000 + 0.5)` |
| Spotify on macOS, track `duration` | integer ms (its dictionary says seconds; it is ms) | none |
| LRC lyric timestamp | minutes, seconds, fraction | defined with lyrics parsing |

The rounding formula is spelled out because languages round halves differently
(Python's `round` rounds half to even, Kotlin's `Math.round` rounds half up).
Positions are never negative, so `floor(x + 0.5)` gives one answer everywhere.

## 2. Statuses

Every jump decision carries exactly one status. Only `personal` and `chorus`
carry a target. Every other status carries none, and the app tells the user why
instead of jumping somewhere random.

| Status | Target | Meaning |
|---|---|---|
| `personal` | yes | from the listener's own heatmap |
| `chorus` | yes | from repeated lyric lines |
| `no_lyrics` | no | LRCLIB has the track, but no time-synced lyrics |
| `instrumental` | no | LRCLIB marks the track as instrumental |
| `not_found` | no | LRCLIB has no matching track |
| `player_unavailable` | no | Spotify is not running or has no track loaded |

A target is never negative.
