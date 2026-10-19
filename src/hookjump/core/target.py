"""Decide where a jump lands, and say why (spec/ALGORITHM.md, section 5)."""

from hookjump.core.models import Favorite, JumpResult, Pin, Status

# Targets are measured a little late: the hotkey is pressed after the part
# starts, and a seek's arrival is read at the next poll. Landing this much
# earlier keeps the first word.
LEAD_MS = 1500


def choose(
    *,
    track_loaded: bool,
    pin: Pin | None,
    favorite: Favorite | None,
) -> JumpResult:
    """The jump for the playing track: the pin, else the learned favorite."""
    if not track_loaded:
        return JumpResult(Status.PLAYER_UNAVAILABLE)
    if pin is not None:
        if pin.typed:  # the listener said exactly where
            return JumpResult(Status.PINNED, pin.at_ms)
        return JumpResult(Status.PINNED, _lead(pin.at_ms))
    if favorite is not None:
        return JumpResult(Status.PERSONAL, _lead(favorite.target_ms))
    return JumpResult(Status.NO_DATA)


def _lead(target_ms: int) -> int:
    return max(0, target_ms - LEAD_MS)
