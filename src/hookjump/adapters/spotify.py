"""The Spotify turntable for macOS: Apple events, run in-process with PyObjC.

Spotify answers one property per Apple event, at about 17 ms each, so a poll
asks only what changes every second: the track id, then the app's properties
record (state and position in a single event). Title, artist and duration are
asked only when the track changes. Measured on macOS 26 with Spotify 1.3.

The id and the properties come from two events, a few milliseconds apart. If
the track changes in between, the reading pairs the old id with the new
track's position, which is near 0; seek detection takes that for a restart and
ignores it. That is why the id is read first.
"""

import math

from AppKit import NSWorkspace
from Foundation import NSAppleScript

from hookjump.adapters.turntable import Reading

BUNDLE_ID = "com.spotify.client"
_TIMEOUT_S = 2  # a hung Spotify must not freeze polling (the default is 2 min)

_POLL = f"""
if application id "{BUNDLE_ID}" is not running then return "not running"
tell application id "{BUNDLE_ID}"
    with timeout of {_TIMEOUT_S} seconds
        try
            set trackId to id of current track
        on error
            set trackId to missing value
        end try
        set p to properties
        return {{trackId, (player state of p) as text, player position of p}}
    end timeout
end tell
"""
_TRACK = f"""
tell application id "{BUNDLE_ID}"
    with timeout of {_TIMEOUT_S} seconds
        set t to current track
        return {{id of t, name of t, artist of t, duration of t}}
    end timeout
end tell
"""
_PLACE = """
if application id "{bundle}" is not running then return false
tell application id "{bundle}"
    with timeout of {timeout} seconds
        if player state is stopped then return false
        if (id of current track) is not "{track}" then return false
        set player position to {seconds}
    end timeout
end tell
return true
"""

# Apple event error numbers.
_NOT_PERMITTED = -1743  # the listener has not allowed hookjump to control Spotify
_GONE = {-600, -609, -1712}  # quit, connection lost, timed out


def _code(text: str) -> int:
    return int.from_bytes(text.encode("latin-1"), "big")


_TYPE, _MISSING = _code("type"), _code("msng")


class SpotifyNotInstalled(RuntimeError):
    pass


class Spotify:
    """A turntable for the Spotify desktop app (hookjump.adapters.turntable)."""

    name = "Spotify"

    def __init__(self) -> None:
        # Compiling a script for an app that is not installed can open a
        # "Where is Spotify?" dialog; check first.
        workspace = NSWorkspace.sharedWorkspace()
        if workspace.URLForApplicationWithBundleIdentifier_(BUNDLE_ID) is None:
            raise SpotifyNotInstalled("Spotify is not installed")
        self._poll = _compile(_POLL)
        self._track = _compile(_TRACK)
        self._known: tuple[str, str | None, str | None, int | None] | None = None

    def read(self) -> Reading | None:
        result = _run(self._poll)
        if result is None or result.stringValue() == "not running":
            return None
        track_id = _text(result.descriptorAtIndex_(1))
        playing = _text(result.descriptorAtIndex_(2)) == "playing"
        position = _seconds_to_ms(result.descriptorAtIndex_(3))
        if track_id is None or track_id.startswith("spotify:ad:"):
            # Nothing loaded, or an ad: no track to learn from or jump in.
            return Reading(None, None, None, None, playing, None)
        title, artist, duration = self._details(track_id)
        return Reading(track_id, title, artist, duration, playing, position)

    def place(self, track_id: str, position_ms: int) -> bool:
        source = _PLACE.format(
            bundle=BUNDLE_ID,
            timeout=_TIMEOUT_S,
            track=track_id.replace("\\", "\\\\").replace('"', '\\"'),
            seconds=f"{position_ms / 1000:.3f}",  # always a "." in source
        )
        result = _run(_compile(source))
        return result is not None and result.booleanValue()

    def _details(self, track_id: str) -> tuple[str | None, str | None, int | None]:
        if self._known is None or self._known[0] != track_id:
            result = _run(self._track)
            if result is None or _text(result.descriptorAtIndex_(1)) != track_id:
                return None, None, None  # changed again meanwhile: next poll
            duration = result.descriptorAtIndex_(4)
            self._known = (
                track_id,
                _text(result.descriptorAtIndex_(2)),
                _text(result.descriptorAtIndex_(3)),
                # Spotify's dictionary says seconds; it returns milliseconds.
                None if _missing(duration) else round(duration.doubleValue()),
            )
        return self._known[1:]


def _compile(source: str) -> NSAppleScript:
    script = NSAppleScript.alloc().initWithSource_(source)
    ok, error = script.compileAndReturnError_(None)
    if not ok:
        raise RuntimeError(f"AppleScript did not compile: {dict(error)}")
    return script


def _run(script: NSAppleScript):  # -> NSAppleEventDescriptor | None
    """Run a script; None if Spotify is gone or did not answer in time."""
    result, error = script.executeAndReturnError_(None)
    if error is None:
        return result
    number = int(error.get("NSAppleScriptErrorNumber", 0))
    if number == _NOT_PERMITTED:
        raise PermissionError(
            "hookjump may not control Spotify. Allow it in System Settings >"
            " Privacy & Security > Automation."
        )
    if number in _GONE:
        return None
    raise RuntimeError(
        f"Spotify did not answer: {error.get('NSAppleScriptErrorMessage')}"
    )


def _missing(descriptor) -> bool:
    return descriptor is None or (
        descriptor.descriptorType() == _TYPE and descriptor.typeCodeValue() == _MISSING
    )


def _text(descriptor) -> str | None:
    return None if _missing(descriptor) else descriptor.stringValue()


def _seconds_to_ms(descriptor) -> int | None:
    if _missing(descriptor):
        return None
    return math.floor(descriptor.doubleValue() * 1000 + 0.5)  # ALGORITHM.md §1
