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
| LRC lyric timestamp | minutes, seconds, fraction | section 3 |

The rounding formula is spelled out because languages round halves differently
(Python's `round` rounds half to even, Kotlin's `Math.round` rounds half up).
Positions are never negative, so `floor(x + 0.5)` gives one answer everywhere.

## 2. Statuses

Every jump decision carries exactly one status. Only `pinned`, `personal` and
`chorus` carry a target. Every other status carries none, and the app tells the
user why instead of jumping somewhere random.

| Status | Target | Meaning |
|---|---|---|
| `pinned` | yes | a spot the listener chose by hand for this track |
| `personal` | yes | from the listener's own heatmap |
| `chorus` | yes | from repeated lyric lines (only when chorus guessing is on) |
| `no_data` | no | nothing pinned, not enough listening yet, and chorus guessing is off |
| `no_lyrics` | no | chorus guessing is on; LRCLIB has the track, but no time-synced lyrics |
| `instrumental` | no | chorus guessing is on; LRCLIB marks the track as instrumental |
| `not_found` | no | chorus guessing is on; LRCLIB has no matching track |
| `player_unavailable` | no | Spotify is not running or has no track loaded |

A target is never negative.

When more than one source has a target, the first in this order wins:
`pinned`, then `personal`, then `chorus`. A choice made by hand beats one
learned from listening, and a learned one beats a guess from the lyrics.

Chorus guessing is **off by default**. The listener turns it on in the app;
until then hookjump makes no network request at all, and a track with nothing
pinned or learned gets `no_data`.

## 3. Lyrics parsing (LRC)

Input: the `syncedLyrics` text from LRCLIB. Output: timed lines `(start_ms,
text)` in time order, and `skipped`, the number of input lines that could not
be read. Vectors: `vectors/lrc_parse.json`.

The parser reports and never judges. Whether too many skipped lines make the
lyrics unusable is decided by the caller, not here.

1. Remove one byte-order mark (U+FEFF) at the very start of the text.
2. Split into lines at `\r\n`, `\r` or `\n`. Strip leading and trailing
   whitespace from each line. Blank lines are ignored and not counted.
3. A line that starts with one or more timestamps, with nothing between them,
   is a **timed line**. A timestamp is `[M:SS]`, `[M:SS.F]` or `[M:SS:F]`:
   - `M`: one or more ASCII digits `0-9`, minutes.
   - `SS`: exactly two ASCII digits, seconds. If any timestamp on the line has
     seconds of 60 or more, the whole line is skipped.
   - `F`: one to three ASCII digits, the decimal part of a second. Pad it on
     the right with zeros to three digits: `.5`, `.50` and `.500` are all 500
     ms, and `.05` is 50 ms.
   - Value: `M * 60000 + SS * 1000 + F`.
   Digits are ASCII only, so `[٠٠:٠١.٠٠]` is not a timestamp.
4. The text of a timed line is what follows its timestamps, with every
   word-level timestamp `<M:SS>`, `<M:SS.F>` or `<M:SS:F>` removed, then
   stripped of whitespace. It may be empty: a timed blank line marks where
   singing stops.
5. A line with several timestamps (compressed LRC, used for repeated lines)
   becomes one line per timestamp, all with the same text.
6. A line that is not timed and matches `[KEY:VALUE]`, where `KEY` is one or
   more ASCII letters or `#`, is an **ID tag**. ID tags are ignored and not
   counted, except `offset`:
   - `[offset:N]`, `N` an optional `+` or `-` followed by ASCII digits, after
     trimming whitespace: milliseconds to subtract from every timestamp (a
     positive offset shows lyrics earlier). It applies to all lines, wherever
     the tag stands. If there are several, the last one wins.
   - An `offset` tag whose value is not of that form is skipped and not applied.
7. Every other line counts as skipped. Examples: plain text without a time,
   section headers such as `[Chorus]`, `[00:75.00]...`, `[00:01.0000]...`.
8. Apply the offset: `start = max(0, start - offset)`.
9. Sort the lines by `start`. Sorting is stable: lines with the same start keep
   the order in which they appear in the file (for a compressed line, the order
   of its timestamps).

## 4. Line comparison key

Two lyric lines count as the same line when their **keys** are equal. A key is
only for comparing: it is never shown and never stored. Vectors:
`vectors/line_key.json`.

The steps run in this order; the order matters.

1. **Brackets.** An innermost group is `(` ... `)` with no `(` or `)` inside,
   or `[` ... `]` with no `[` or `]` inside. Replace every innermost group with
   a space, and repeat until none is left; call the result `outside`.
   - If `outside` still contains a word character (step 6), use `outside`: an
     ad-lib such as "love (oh-i-oh)" becomes "love".
   - Otherwise the whole line was bracketed, such as a backing-vocal line
     "(we could have had it all)". Keep its words: use the original line with
     every `(`, `)`, `[` and `]` replaced by a space.

   Measured on 30 development songs (none of them evaluation songs): keeping
   brackets found 899 lines with a repeat, always dropping them 912 (better in
   7 songs, worse in 4), this rule 932 (better in 9, worse in none).
2. **Apostrophes** are removed, joining their words: `'` U+0027, `‘` U+2018,
   `’` U+2019, `` ` `` U+0060, `´` U+00B4, `ʼ` U+02BC. "don't" becomes "dont",
   "Ali'nin" becomes "Alinin". This comes before step 4, which would turn `´`
   into a space.
3. Replace the dotless `ı` (U+0131) with `i`. It is the only Turkish letter
   that step 4 does not reduce to a plain letter.
4. **Fold accents.** Apply Unicode NFKD, then delete every character of
   category Mn (nonspacing marks). `ş` becomes `s`, `İ` becomes `I` without
   the combining dot, `é` becomes `e`, `ﬁ` becomes `fi`, fullwidth `Ｈ`
   becomes `H`.
5. **Lowercase** with Unicode default case mapping, independent of locale.
   Thanks to steps 3 and 4, Turkish casing rules are never needed.
6. **Word characters** are letters (Unicode categories starting with `L`) and
   decimal digits (category `Nd`). Every other character becomes a space:
   punctuation, hyphens, symbols such as `♪`, and numerals that are not decimal
   digits such as `〇`.
7. Collapse runs of spaces into one and trim both ends.

A key may be empty, for a line with no words (only `♪`, for example).
