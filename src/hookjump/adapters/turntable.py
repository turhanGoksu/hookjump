"""The turntable: what every player adapter must do, and nothing more.

A turntable talks to one player app (Spotify on macOS today). It is kept
deliberately simple; the shared parts built on it are the ear, which samples
it, and the needle, which jumps with it (hookjump.playback).

Every turntable keeps three promises:

1. ``read()`` takes one snapshot: the track, the state and the position come
   from a single call to the player, so they belong to the same moment.
2. ``place()`` moves the playhead only if the given track is still loaded,
   checked inside that same call to the player.
3. A value the player does not report is None, never a made-up 0.
"""

from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True)
class Reading:
    """One snapshot of the player. None means the player did not say."""

    track_id: str | None  # None: nothing loaded
    title: str | None
    artist: str | None
    duration_ms: int | None
    playing: bool
    position_ms: int | None


class Turntable(Protocol):
    name: str  # for messages, such as "Spotify"

    def read(self) -> Reading | None:
        """The player's state now, or None if the player app is not running."""
        ...

    def place(self, track_id: str, position_ms: int) -> bool:
        """Move the playhead if ``track_id`` is still loaded; False if not."""
        ...
