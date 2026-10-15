# hookjump

Press a hotkey while a song plays in Spotify on macOS, and playback jumps to the
best part of the song.

> **Status:** early development. Nothing to install yet.

## How it finds "the best part"

1. **Your pick:** while a song plays in the background, press a second hotkey
   (or type a time such as `1:23`) to pin a spot: a chorus, a verse you love, a
   solo. A pinned spot always wins.
2. **Your own listening:** hookjump notes, on your Mac only, where you rewind
   to and what you replay. As this builds up, it jumps to the part you listen
   to most.
3. **Chorus guess (optional, off by default):** if you turn it on, hookjump
   fetches time-synced lyrics from [LRCLIB](https://lrclib.net) and guesses the
   chorus from repeated lines, for songs you have not pinned or replayed yet.

When there is no target (nothing pinned or learned yet, or no lyrics for the
chorus guess), hookjump tells you so instead of jumping somewhere random.

## Privacy

Everything stays on your Mac. Out of the box hookjump makes no network
request at all. Only if you turn on the chorus guess does it look up the
playing song on LRCLIB, and lyrics text is never stored.

What hookjump keeps, in one file in `~/Library/Application Support/hookjump/`:

- the spots you pin;
- where you rewound or skipped to in each song, for its last 10 listens with
  such a seek, with no dates or times;
- the raw playback positions it learns from, deleted after 7 days.

The exact format is in [spec/STORAGE.md](spec/STORAGE.md).

## Repository layout

- `src/hookjump/core/`: the platform-independent algorithms, pure functions
  using only the Python standard library.
- `spec/`: the same algorithms in plain language, plus JSON test vectors that
  every implementation (this one, a future Android one) must pass.

## License

[Apache-2.0](LICENSE)
