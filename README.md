# hookjump

Press a hotkey while a song plays in Spotify on macOS, and playback jumps to the
best part of the song: the part you pinned, or the part you keep going back to.

> **Status:** v0.1 in development. It works and is used daily on its author's
> Mac (macOS 26, Apple Silicon); see [Compatibility](#compatibility) for what
> has and has not been tried.

## How it finds "the best part"

1. **Your pick:** while a song plays in the background, press ⌥D to pin that
   spot (a chorus, a verse you love, a solo), or type a time such as `1:23`. A
   pinned spot always wins.
2. **Your own listening:** hookjump notes, on your Mac only, where you rewind
   to and where you skip to. Once the same place comes up in two different
   listens, ⌥F jumps there. Listening without touching anything teaches it
   nothing, and its own jumps never count.
3. **Chorus guess:** planned as an option, off by default. Not built yet.

When there is nothing to jump to, hookjump says so instead of jumping somewhere
random.

## Install

You need:

- macOS 13 or later, and the **Spotify desktop app** (not the web player);
- Python 3.10 or later. The `python3` that comes with macOS is 3.9, too old;
  the steps below get a newer one through Homebrew;
- for `hookjump.app` (open at login, permission in hookjump's own name):
  Apple's Command Line Tools. If you use Homebrew you already have them.

```sh
brew install pipx
pipx ensurepath          # then open a new terminal
pipx install hookjump
hookjump install         # builds ~/Applications/hookjump.app
open ~/Applications/hookjump.app
```

pipx gives hookjump a Python environment of its own, so nothing it needs
mixes with your other tools, and `pipx uninstall` removes all of it.
`uv tool install hookjump` does the same, if you use uv. The version in
development: `pipx install git+https://github.com/turhanGoksu/hookjump`.

`hookjump install` compiles a small launcher from its C source on your Mac
([launcher.c](src/hookjump/launcher/launcher.c), about 100 lines), so no
prebuilt binary is ever downloaded. Then choose **Open at Login** in the hook's
menu to start hookjump with your Mac.

Without the Command Line Tools, `hookjump install` says how to get them
(`xcode-select --install`). Until then, `hookjump run` in a terminal does the
same, for as long as the terminal stays open.

## Using it

A hook sits in the menu bar. It carries bait when hookjump knows a spot in the
playing song, is empty when it does not yet, and fades while learning is
paused.

| Keys | |
|---|---|
| ⌥F | jump to the best part (it lands 1.5 s early, so you hear the start) |
| ⌥D | pin the spot that is playing |

After a key, a line opens next to the hook for 3 seconds: `●` is where the song
is, `◆` where it jumps, `◉` both at once, `✕` nowhere to jump.

Click the hook for its menu. At the top, the song is drawn as a line with what
hookjump learned on it: ticks above where you rewound to, ticks below where you
skipped to, `◆` the hook, `●` playback. Click the line to pin that second.
Below it:

- **This Song:** pin at a typed time, remove the pin, forget the song, or stop
  learning from it (a pin still works);
- **Pause Learning**, **Open at Login**;
- **Shortcuts:** change ⌥F and ⌥D. The recorder refuses keys you would miss:
  ones that type a character on your keyboard (on a Turkish keyboard ⌥Q is
  `@`), accent keys, and shortcuts every app uses, such as ⌘C;
- **Delete Everything…**

The same jump and pin work from a terminal or any tool that runs commands,
such as a keyboard shortcut made in the Shortcuts app:

```sh
hookjump jump    # Jumped to 1:43: the part you replay most.
hookjump pin     # Pinned 1:12. The jump lands 1.5 s before it.
```

## Permissions

hookjump asks for one permission: to control Spotify, so it can read which
song is playing and where, and move it. macOS asks the first time, in
hookjump's name. It needs no Accessibility or Input Monitoring permission: its
hotkeys use the same system call as other shortcut apps, which asks for none.

If hookjump runs from a folder macOS protects (Desktop, Documents, Downloads),
macOS also asks to let it read that folder. Installing with pipx avoids that.

## Privacy

Everything stays on your Mac. hookjump makes no network request at all.

What hookjump keeps, in one file in `~/Library/Application Support/hookjump/`:

- the spots you pin, and your shortcuts;
- where you rewound or skipped to in each song, for its last 10 listens with
  such a seek, with no dates or times;
- the raw playback positions it learns from, deleted after 7 days.

Pause Learning stops all recording; This Song › Forget, and Delete Everything,
erase it (deleted data is overwritten, not left in the file). The exact format
is in [spec/STORAGE.md](spec/STORAGE.md).

## Compatibility

| | |
|---|---|
| macOS 26 (Tahoe), Apple Silicon | used daily |
| macOS 15 (Sequoia), Apple Silicon and Intel | automatic tests only |
| macOS 15.0 and 15.1 | these refuse hotkeys with ⌥ alone, so hookjump falls back to ⌃⌥F and ⌃⌥D and says so in its menu |
| macOS 13 and 14 | not tried |
| Python 3.10 to 3.15 | 3.10 and 3.15 tested automatically; 3.15 used daily |
| Spotify 1.3 | used daily |

The automatic tests run on GitHub's machines with fake players: they check
hookjump's own code, not Spotify.

## Known limits

- **Spotify on macOS only**, through its AppleScript support. Apple Music,
  Windows and Android are planned, not built.
- Spotify playing on another device (Spotify Connect) may not answer.
- A hotkey another app also uses cannot be detected: macOS lets two apps
  register the same one. Change hookjump's under Shortcuts if they clash.
- The recorder treats a letter typed with ⌥ as rare, like ⌥F's `ƒ`. On
  keyboards that type their own letters with ⌥ (Polish Pro: ⌥A is `ą`), it
  would accept such a key and take that letter away.

## Uninstall

```sh
hookjump uninstall      # removes hookjump.app, its login item and permission
pipx uninstall hookjump
rm -r ~/Library/Application\ Support/hookjump    # what it learned
```

## Repository layout

- `src/hookjump/core/`: the platform-independent algorithms, pure functions
  using only the Python standard library.
- `src/hookjump/adapters/`: storage (SQLite), the Spotify turntable, hotkeys,
  the keyboard layout.
- `src/hookjump/`: the booth that joins them, the menu bar app, the commands.
- `spec/`: the algorithms and the storage format in plain language, plus JSON
  test vectors that every implementation (this one, a future Android one)
  must pass.
- `eval/`: the evaluation protocol and its labels, written before any
  detector ran on those songs.

## License

[Apache-2.0](LICENSE)
