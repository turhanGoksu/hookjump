"""Plain data passed into and out of the core."""

from dataclasses import dataclass
from enum import Enum


class Status(str, Enum):
    """Where a jump target came from, or why there is none.

    The values are the strings used in spec/ JSON vectors; renaming one breaks
    every vector and every other implementation.
    """

    PINNED = "pinned"  # a spot the listener chose by hand
    PERSONAL = "personal"  # from the listener's own heatmap
    CHORUS = "chorus"  # from repeated lyric lines
    NO_LYRICS = "no_lyrics"  # LRCLIB has the track, but no time-synced lyrics
    INSTRUMENTAL = "instrumental"  # LRCLIB marks the track as instrumental
    NOT_FOUND = "not_found"  # LRCLIB has no matching track
    PLAYER_UNAVAILABLE = "player_unavailable"  # Spotify closed or nothing loaded

    @property
    def has_target(self) -> bool:
        return self in (Status.PINNED, Status.PERSONAL, Status.CHORUS)


@dataclass(frozen=True)
class LyricLine:
    """One timed line of lyrics. The text stays in memory and is never stored."""

    start_ms: int
    text: str  # may be empty: a timed blank line marks where singing stops


@dataclass(frozen=True)
class ParsedLyrics:
    """Lines in time order, plus how many input lines could not be read.

    The parser reports and never judges: whether too many skipped lines make
    the lyrics unusable is decided by the caller.
    """

    lines: tuple[LyricLine, ...]
    skipped: int


@dataclass(frozen=True)
class JumpResult:
    """A jump decision: a target, or an explicit reason why there is none.

    The two always agree: ``pinned``, ``personal`` and ``chorus`` carry a
    target, every other status carries none. hookjump never jumps without
    saying why, and never stays silent when it does not jump.
    """

    status: Status
    target_ms: int | None = None

    def __post_init__(self) -> None:
        if self.status.has_target and self.target_ms is None:
            raise ValueError(f"status {self.status.value!r} needs a target_ms")
        if not self.status.has_target and self.target_ms is not None:
            raise ValueError(f"status {self.status.value!r} must not carry a target_ms")
        if self.target_ms is None:
            return
        # bool is a subclass of int, and a float means a unit slipped through.
        if type(self.target_ms) is not int:
            raise TypeError(
                f"target_ms must be int milliseconds, got {self.target_ms!r}"
            )
        if self.target_ms < 0:
            raise ValueError(f"target_ms must be >= 0, got {self.target_ms}")
