# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/). While the version is 0.x, minor
releases may change how it behaves.

A released version is never changed: fixes ship as a new version.

## [Unreleased]

### Removed

- The planned chorus guess, before it was built, and the lyrics parser that
  shipped unused for it in 0.1.0. hookjump jumps only to a spot the listener
  pinned or keeps going back to, and never goes online.

## [0.1.0] - 2026-10-19

The first release: Spotify on macOS.

### Added

- ⌥F jumps to the best part of the song playing in Spotify, 1.5 s early so
  the start is heard; ⌥D pins the spot that is playing. A pin always wins.
- Learning from listening: where the listener rewinds or skips to, once the
  same place comes up in two different listens (the last 10 listens with such
  a seek count). Untouched listening and hookjump's own jumps teach nothing.
- No jump without a reason: with nothing pinned or learned, hookjump says so
  instead of jumping somewhere random.
- A menu bar app: a hook that shows whether the playing song's spot is known,
  a line after each hotkey that shows the jump, and a menu that draws the song
  with what was learned on it; a click on that line pins a second.
- Privacy controls: pause learning, forget a song, stop learning from a song,
  delete everything. Nothing leaves the Mac; raw positions are kept 7 days,
  and kept data has no dates or times (spec/STORAGE.md).
- Changeable shortcuts. The recorder refuses keys that would stop the
  listener typing a character on their keyboard, accent keys, and shortcuts
  every app uses. Where macOS refuses ⌥ alone (15.0 and 15.1), ⌃⌥F and ⌃⌥D.
- `hookjump install` builds ~/Applications/hookjump.app from source on the
  Mac: it opens at login, and macOS asks to control Spotify in hookjump's
  name, not Python's. `hookjump uninstall` removes it and its permission.
- `hookjump jump` and `hookjump pin` reach the running app without Carbon
  hotkeys, so a shortcut made in the Shortcuts app can trigger them.
- spec/: the algorithms, the storage format and JSON test vectors, for
  implementations on other platforms.

[Unreleased]: https://github.com/turhanGoksu/hookjump/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/turhanGoksu/hookjump/releases/tag/v0.1.0
