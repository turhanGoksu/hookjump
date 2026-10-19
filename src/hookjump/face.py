"""What hookjump shows in the menu bar: a talking hook, and a line on demand.

Normally only the hook icon is there, and its look tells the state. When a
hotkey is pressed, a line of the playing track opens next to it for about 3
seconds and shows the jump: ● where playback is, ◆ where the jump lands, ◉
both at once. Then it closes again, so the menu bar stays narrow (a MacBook's
notch hides wide items) and no notification is needed.

Plain text and data only; drawing the icon is in hookjump.icon.
"""

from dataclasses import dataclass
from enum import Enum

from hookjump.core.models import JumpResult
from hookjump.playback import Landing

WIDTH = 11  # cells of the line; one cell is about a tenth of the track
SHOW_S = 3.0  # how long the line stays open
BEFORE_S = 0.8  # how long the position before a jump is shown first

TRACK, HERE, MARK, BOTH, NONE = "━", "●", "◆", "◉", "✕"


class Mood(Enum):
    """The look of the hook icon."""

    KNOWN = "known"  # a full hook: this track's spot is known (pinned or learned)
    UNKNOWN = "unknown"  # an empty hook: nothing known yet for this track
    RESTING = "resting"  # a faded hook: learning paused, or no track playing


def mood(*, playing_track: bool, learning_paused: bool, spot_known: bool) -> Mood:
    if not playing_track or learning_paused:
        return Mood.RESTING
    return Mood.KNOWN if spot_known else Mood.UNKNOWN


@dataclass(frozen=True)
class Frame:
    text: str  # shown next to the hook; "" shows the hook alone
    seconds: float


def line(position_ms: int | None, duration_ms: int | None, mark_ms: int | None) -> str:
    """The track as WIDTH cells; ✕ in the last cell when there is no mark."""
    if not duration_ms or duration_ms <= 0:
        return ""
    cells = [TRACK] * WIDTH
    if mark_ms is None:
        cells[-1] = NONE
    else:
        cells[_cell(mark_ms, duration_ms)] = MARK
    if position_ms is not None:
        here = _cell(position_ms, duration_ms)
        cells[here] = BOTH if cells[here] == MARK else HERE
    return "".join(cells)


def jump_frames(
    before_ms: int | None,
    duration_ms: int | None,
    result: JumpResult,
    landing: Landing | None,
) -> list[Frame]:
    """The line after the jump hotkey; ``landing`` is None when nothing moved."""
    if result.target_ms is None:  # no_data or player_unavailable
        # With no track to draw (Spotify closed), the cross alone still answers.
        text = line(before_ms, duration_ms, None) or NONE
        return [Frame(text, SHOW_S), Frame("", 0)]
    before = line(before_ms, duration_ms, result.target_ms)
    if landing is Landing.LANDED:
        after = line(result.target_ms, duration_ms, result.target_ms)
    else:  # slipped, or the track changed meanwhile: the mark stays unreached
        after = before + " " + NONE
    return [Frame(before, BEFORE_S), Frame(after, SHOW_S - BEFORE_S), Frame("", 0)]


def pin_frames(
    position_ms: int | None, duration_ms: int | None, old_pin_ms: int | None
) -> list[Frame]:
    """The line after the pin hotkey: the mark moves to where playback is."""
    before = line(position_ms, duration_ms, old_pin_ms)
    after = line(position_ms, duration_ms, position_ms)
    return [Frame(before, BEFORE_S), Frame(after, SHOW_S - BEFORE_S), Frame("", 0)]


def _cell(ms: int, duration_ms: int) -> int:
    return min(WIDTH - 1, max(0, ms * WIDTH // duration_ms))
