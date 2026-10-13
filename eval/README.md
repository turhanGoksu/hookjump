# Evaluation

Does hookjump's chorus guess land on the part of a song the listener likes
best? This folder holds the labels and, later, the result.

## Protocol

The labels are fixed before the detector ever runs on these songs, the same
way as a pre-registered study:

1. Pick 30 songs you know well: Turkish and English mixed, different genres.
   Do not choose them by looking at their lyrics or at hookjump's output.
2. Label each one while it plays in Spotify:
   `.venv/bin/python eval/label.py`. Record the moment you like best and why.
3. Commit `labels.csv` before the chorus detector runs on any of these songs.
   The commit date is the proof.
4. Develop and tune the detector only on other songs (the dev set). A song in
   `labels.csv` is never used for tuning.
5. Run the detector on the 30 songs once and report the result as it comes
   out, misses and "no target" answers included.

The tolerance (how close to the label counts as a hit) and the metrics are
fixed and written here before step 5: at least "lands near the start" and
"lands inside the part".

## How to label a song

The label answers one question: if a hotkey could jump to one moment of this
song, which moment? Mark where that part **begins** (the first word of the
chorus, the first note of the solo, the drop), not its middle, and where it
**ends**. It is your judgement, not a measurement of your listening history.

The start is what a jump is judged against. The end tells whether a jump that
misses the start still lands inside the part, and is needed for features such
as looping a part.

1. Play the song in Spotify and skip to just before your favorite part.
2. Pause the moment it begins, so the position does not move while you type.
3. Run `.venv/bin/python eval/label.py`. It shows the paused position.
4. Type `p` to play from there and listen. Adjust with `-2` or `+1` (seconds),
   an exact time such as `1:08`, or `n` for wherever Spotify is now, and `p`
   again until it starts right. Then press Enter.
5. The part plays. Press Enter where it ends, then check it the same way (`p`
   plays the last 5 seconds before the end).
6. Answer why (chorus, verse, ...) and the language.

To add an end to an older label, play that song and run the script again:
answer `y` to "Replace it?" and it offers the earlier start, why and language,
so Enter keeps them.

Rules:

- If the part repeats (a chorus sung three times), mark the occurrence you like
  best, even if it is the last one. If you like them all the same, mark the
  earliest.
- If your favorite part spans sections (a bridge that flows into the chorus),
  mark where it begins and choose the section it begins with (`bridge`).
  `other` is only for parts that fit none of the sections.
- Label each song once. Spotify has separate tracks for a single and an album
  release, and their timings can differ; label the one you listen to. The
  script warns when another release of the song is already labeled.
- If you are not sure where the best part is, leave the song out and pick
  another one.
- If the script says Spotify reports 0:00 although you paused later, press
  play for a moment, pause again and rerun. Spotify can report 0 for a track
  that has not played yet.

## labels.csv

| Column | Meaning |
|---|---|
| `spotify_id` | Spotify track URI, so another release of the same song is not mixed up |
| `title`, `artist` | as Spotify shows them |
| `duration_ms` | track length, integer milliseconds |
| `best_part_ms` | where the favorite part begins, integer milliseconds |
| `best_part` | the same moment as `m:ss.s`, for reading |
| `end_ms`, `end` | where the part ends, the same two ways; empty for labels made before ends were recorded |
| `why` | `chorus`, `verse`, `bridge`, `solo`, `drop`, `intro` or `other` |
| `lang` | language of the song's lyrics, `tr`, `en` or `other`, even when the labeled part is instrumental (a solo); `other` also covers songs without lyrics |

No lyrics are stored here, only titles and times.
