"""Decide where a jump lands, and say why (spec/ALGORITHM.md, section 7)."""

from hookjump.core.models import Favorite, JumpResult, Pin, Status

# Most targets are measured a little late: the hotkey is pressed after the
# part starts, a seek's arrival is read at the next poll, a lyric timestamp
# trails its first word. Landing this much earlier keeps the first word.
LEAD_MS = 1500

_CHORUS_STATUSES = frozenset(
    {Status.CHORUS, Status.NO_LYRICS, Status.INSTRUMENTAL, Status.NOT_FOUND}
)


def choose(
    *,
    track_loaded: bool,
    pin: Pin | None,
    favorite: Favorite | None,
    chorus: JumpResult | None,
) -> JumpResult:
    """The jump for the playing track: pinned, then personal, then chorus.

    ``chorus`` is the chorus guess with the line start as its target, before
    any lead-in, or None when the listener has chorus guessing off.
    """
    if chorus is not None and chorus.status not in _CHORUS_STATUSES:
        raise ValueError(f"not a chorus guess status: {chorus.status.value!r}")

    if not track_loaded:
        return JumpResult(Status.PLAYER_UNAVAILABLE)
    if pin is not None:
        if pin.typed:  # the listener said exactly where
            return JumpResult(Status.PINNED, pin.at_ms)
        return JumpResult(Status.PINNED, _lead(pin.at_ms))
    if favorite is not None:
        return JumpResult(Status.PERSONAL, _lead(favorite.target_ms))
    if chorus is None:
        return JumpResult(Status.NO_DATA)
    if chorus.target_ms is None:
        return chorus  # no_lyrics, instrumental or not_found: say why
    return JumpResult(Status.CHORUS, _lead(chorus.target_ms))


def _lead(target_ms: int) -> int:
    return max(0, target_ms - LEAD_MS)
