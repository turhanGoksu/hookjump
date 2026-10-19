"""Label the best part of the song playing in Spotify, for the evaluation.

Play a song, pause where your favorite part begins, then run:

    .venv/bin/python eval/label.py

The script reads the track and the position from Spotify and lets you listen
again and adjust the start. Then it plays the part so you can mark where it
ends, and writes one row to eval/labels.csv. It never looks at lyrics or at
what hookjump learned: labels are fixed before any method is judged against
them (see eval/README.md).
"""

import argparse
import csv
import json
import math
import re
import subprocess
import sys
from pathlib import Path

LABELS = Path(__file__).resolve().parent / "labels.csv"
FIELDS = [
    "spotify_id",
    "title",
    "artist",
    "duration_ms",
    "best_part_ms",
    "best_part",
    "end_ms",
    "end",
    "why",
    "lang",
]
WHY = {
    "c": "chorus",
    "v": "verse",
    "b": "bridge",
    "s": "solo",
    "d": "drop",
    "i": "intro",
    "o": "other",
}
LANG = {"t": "tr", "e": "en", "o": "other"}
GOAL = 30

# JXA returns JSON, so numbers keep a "." whatever the system locale.
_READ = """
function run() {
  const s = Application("Spotify");
  if (!s.running()) return "null";
  const t = s.currentTrack;
  return JSON.stringify({
    state: s.playerState(), position: s.playerPosition(), id: t.id(),
    title: t.name(), artist: t.artist(), duration_ms: t.duration()
  });
}
"""
# Both check the track first: the song may change while the listener types.
# Setting the position does not start a paused player, so _PLAY also plays.
_PLAY = """
function run(argv) {
  const s = Application("Spotify");
  if (s.currentTrack.id() !== argv[0]) return "track changed";
  s.playerPosition = parseFloat(argv[1]);
  s.play();
  return "ok";
}
"""
_POSITION = """
function run(argv) {
  const s = Application("Spotify");
  if (s.currentTrack.id() !== argv[0]) return "track changed";
  return JSON.stringify(s.playerPosition());
}
"""
_CLOCK = re.compile(r"([0-9]+):([0-9]{1,2}(?:\.[0-9]+)?)")
_RELATIVE = re.compile(r"[+-][0-9]+(?:\.[0-9]+)?")
_SECONDS = re.compile(r"[0-9]+(?:\.[0-9]+)?")


def osa(script: str, *args: str) -> str:
    result = subprocess.run(
        ["osascript", "-l", "JavaScript", "-e", script, *args],
        capture_output=True,
        text=True,
        timeout=30,
    )
    if result.returncode != 0:
        sys.exit(f"Could not talk to Spotify: {result.stderr.strip()}")
    return result.stdout.strip()


def to_ms(seconds: float) -> int:
    return math.floor(seconds * 1000 + 0.5)  # spec/ALGORITHM.md, section 1


def clock(ms: int) -> str:
    return f"{ms // 60_000}:{ms % 60_000 / 1000:04.1f}"


def parse_moment(text: str, current_ms: int, duration_ms: int) -> int | None:
    """``1:08`` or ``68`` sets the moment; ``-3`` or ``+2.5`` moves it."""
    if _RELATIVE.fullmatch(text):
        ms = current_ms + to_ms(float(text))
    elif match := _CLOCK.fullmatch(text):
        if float(match[2]) >= 60:
            return None
        ms = int(match[1]) * 60_000 + to_ms(float(match[2]))
    elif _SECONDS.fullmatch(text):
        ms = to_ms(float(text))
    else:
        return None
    return min(max(ms, 0), duration_ms)


def song_key(title: str, artist: str) -> tuple[str, str]:
    """The same song across releases: "Flying - Remastered" is "flying"."""
    title = re.sub(r"\s*[(\[].*?[)\]]", "", title).split(" - ")[0]
    return title.casefold().strip(), artist.casefold().strip()


def choose(prompt: str, options: dict[str, str], default: str = "") -> str:
    keys = "  ".join(f"[{k}] {v}" for k, v in options.items())
    keep = f"  [Enter] keep {default}" if default else ""
    while True:
        answer = input(f"  {prompt}  {keys}{keep}: ").strip().lower()
        if answer in options:
            return options[answer]
        if not answer and default:
            return default


def play_from(track_id: str, ms: int) -> None:
    if osa(_PLAY, track_id, f"{ms / 1000:.3f}") != "ok":
        sys.exit("The song changed in Spotify. Nothing saved.")


def position_now(track_id: str) -> int:
    out = osa(_POSITION, track_id)
    if out == "track changed":
        sys.exit("The song changed in Spotify. Nothing saved.")
    seconds = json.loads(out)
    if seconds is None:
        sys.exit("Spotify did not report a position. Nothing saved.")
    return to_ms(seconds)


def adjust(name: str, ms: int, now: dict, lead_ms: int) -> int:
    """Let the listener check a moment and move it; returns the moment in ms.

    ``p`` plays from ``lead_ms`` before the moment, so an end can be heard
    arriving; ``n`` takes wherever Spotify is now.
    """
    hint = "play from here" if lead_ms == 0 else f"play the last {lead_ms // 1000} s"
    while True:
        answer = (
            input(
                f"  {name} {clock(ms)}   [Enter] keep  [p] {hint}  [n] now"
                "  or type 1:08 / 68 / -3 / +2: "
            )
            .strip()
            .lower()
        )
        if not answer:
            return ms
        if answer == "p":
            play_from(now["id"], max(0, ms - lead_ms))
        elif answer == "n":
            ms = position_now(now["id"])
        elif (moved := parse_moment(answer, ms, now["duration_ms"])) is not None:
            ms = moved
        else:
            print("  Not a time. Examples: 1:08, 68, -3, +2")


def read_spotify() -> dict:
    now = json.loads(osa(_READ))
    if now is None:
        sys.exit("Spotify is not running.")
    if now["state"] == "stopped":
        sys.exit("Nothing is loaded in Spotify. Play a song first.")
    if now["id"].startswith("spotify:ad:"):
        sys.exit("An ad is playing. Try again after it.")
    if now["position"] is None:  # seen for a moment while tracks change
        sys.exit("Spotify did not report a position. Try again in a second.")
    return now


def load(path: Path) -> list[dict]:
    if not path.exists():
        return []
    with path.open(newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def save(path: Path, rows: list[dict]) -> None:
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=FIELDS, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--out", type=Path, default=LABELS, help="labels CSV")
    out = parser.parse_args().out

    now = read_spotify()
    rows = load(out)
    print(f"▶ {now['title']} — {now['artist']}  ({now['id']})")
    start = to_ms(now["position"])
    from_label = False
    old: dict = {}  # the earlier label of this song, if any

    # Spotify has separate tracks for a single and an album release of the
    # same song; each song is labeled once.
    key = song_key(now["title"], now["artist"])
    earlier = [
        row
        for row in rows
        if row["spotify_id"] == now["id"]
        or song_key(row["title"], row["artist"]) == key
    ]
    if earlier:
        same_track = any(row["spotify_id"] == now["id"] for row in earlier)
        what = "This track" if same_track else "Another release of this song"
        old = earlier[0]
        span = (
            f"{old['best_part']}–{old['end']}" if old.get("end") else old["best_part"]
        )
        print(f"  {what} is already labeled: {old['title']} at {span}.")
        if input("  Replace it? [y/N]: ").strip().lower() != "y":
            sys.exit("Nothing saved.")
        rows = [row for row in rows if row not in earlier]
        if same_track:  # offer the earlier start; [n] takes Spotify's instead
            start, from_label = int(old["best_part_ms"]), True

    if start < 1000 and not from_label:
        # Two labels were saved as 0:00.0 although the listener had paused
        # much later: Spotify can report 0 for a track that has not played yet.
        print(
            f"  ! Spotify reports {clock(start)}. If you paused later in the song,"
            " press play for a moment, pause again and rerun."
        )
        if input("  Is the very start really the moment? [y/N]: ").strip() != "y":
            sys.exit("Nothing saved.")
    start = adjust("starts at", start, now, lead_ms=0)

    play_from(now["id"], start)
    input("  Playing the part. Press Enter where it ends: ")
    end = position_now(now["id"])
    while True:
        end = adjust("ends at", end, now, lead_ms=5000)
        if end > start:
            break
        print(f"  The end must come after the start ({clock(start)}).")

    row = {
        "spotify_id": now["id"],
        "title": now["title"],
        "artist": now["artist"],
        "duration_ms": now["duration_ms"],
        "best_part_ms": start,
        "best_part": clock(start),
        "end_ms": end,
        "end": clock(end),
        "why": choose("why here?", WHY, old.get("why", "")),
        "lang": choose("language?", LANG, old.get("lang", "")),
    }
    save(out, [*rows, row])
    missing = sum(1 for r in rows if not r.get("end_ms"))
    note = f", {missing} still without an end" if missing else ""
    print(
        f"✓ Saved {clock(start)}–{clock(end)} to {out.name}"
        f" ({len(rows) + 1}/{GOAL}{note})"
    )


if __name__ == "__main__":
    try:
        main()
    except (KeyboardInterrupt, EOFError):
        sys.exit("\nNothing saved.")
