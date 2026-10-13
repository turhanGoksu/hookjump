# hookjump

Press a hotkey while a song plays in Spotify on macOS, and playback jumps to the
best part of the song.

> **Status:** early development. Nothing to install yet.

## How it finds "the best part"

1. **Chorus:** hookjump fetches time-synced lyrics from
   [LRCLIB](https://lrclib.net) and looks for repeated lines.
2. **Your own listening:** it records, on your Mac only, where you rewind to and
   what you replay. After a few listens, your favorite moment wins over the
   chorus guess.
3. **Your pick:** while a song plays in the background, press a second hotkey
   to pin that spot (a verse you love, a solo). A pinned spot always wins.

When there is no target (no lyrics, an instrumental, a song LRCLIB does not
know), hookjump tells you so instead of jumping somewhere random.

## Privacy

Everything stays on your Mac. The only network request is the LRCLIB lookup,
and lyrics text is never stored.

## Repository layout

- `src/hookjump/core/`: the platform-independent algorithms, pure functions
  using only the Python standard library.
- `spec/`: the same algorithms in plain language, plus JSON test vectors that
  every implementation (this one, a future Android one) must pass.

## License

[Apache-2.0](LICENSE)
