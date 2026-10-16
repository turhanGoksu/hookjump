"""What hookjump says, in words: for the menu and for the command line."""

from hookjump.core.models import Status

_REASONS = {
    Status.PINNED: "your pinned spot",
    Status.PERSONAL: "the part you replay most",
    Status.CHORUS: "the chorus, guessed from the lyrics",
    Status.NO_DATA: "nothing pinned or learned for this song yet",
    Status.NO_LYRICS: "no synced lyrics for this song",
    Status.INSTRUMENTAL: "an instrumental, so no chorus to guess",
    Status.NOT_FOUND: "this song's lyrics were not found",
    Status.PLAYER_UNAVAILABLE: "Spotify is not playing anything",
}
_LANDINGS = {
    "slipped": "Spotify did not move there",
    "track_changed": "the song changed meanwhile",
    "no_player": "Spotify closed meanwhile",
}


def clock(ms: int) -> str:
    """1:43 for 103500; seconds are cut, not rounded, like players show them."""
    return f"{ms // 60_000}:{ms % 60_000 // 1000:02d}"


def describe_jump(status: str, target_ms: int | None, landing: str | None) -> str:
    reason = _REASONS[Status(status)]
    if target_ms is None:
        return f"No jump: {reason}."
    if landing and landing != "landed":
        return f"Tried {clock(target_ms)} ({reason}), but {_LANDINGS[landing]}."
    return f"Jumped to {clock(target_ms)}: {reason}."


def describe_pin(pinned_ms: int | None) -> str:
    if pinned_ms is None:
        return "Nothing pinned: Spotify is not playing anything."
    return f"Pinned {clock(pinned_ms)}. The jump lands 1.5 s before it."
