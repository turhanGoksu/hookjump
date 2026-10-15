"""The Spotify turntable without Spotify: Apple event replies built by hand.

macOS only (PyObjC); skipped elsewhere. The real app was tried by hand.
"""

import pytest

pytest.importorskip("Foundation")

from Foundation import NSAppleEventDescriptor  # noqa: E402

from hookjump.adapters import spotify  # noqa: E402
from hookjump.adapters.spotify import Spotify  # noqa: E402
from hookjump.adapters.turntable import Reading  # noqa: E402


def text(value: str) -> NSAppleEventDescriptor:
    return NSAppleEventDescriptor.descriptorWithString_(value)


def real(value: float) -> NSAppleEventDescriptor:
    return NSAppleEventDescriptor.descriptorWithDouble_(value)


def missing() -> NSAppleEventDescriptor:
    return NSAppleEventDescriptor.descriptorWithTypeCode_(spotify._code("msng"))


def a_list(*items: NSAppleEventDescriptor) -> NSAppleEventDescriptor:
    result = NSAppleEventDescriptor.listDescriptor()
    for index, item in enumerate(items, start=1):
        result.insertDescriptor_atIndex_(item, index)
    return result


class Replies:
    """Stands in for _run: the next reply to each script, and a call count."""

    def __init__(self, poll: object, track: object = None) -> None:
        self.poll, self.track, self.track_calls = poll, track, 0

    def __call__(self, script: object) -> object:
        if script == "track":
            self.track_calls += 1
            return self.track
        return self.poll


def turntable(monkeypatch: pytest.MonkeyPatch, replies: Replies) -> Spotify:
    monkeypatch.setattr(spotify, "_run", replies)
    player = Spotify.__new__(Spotify)  # skip the install check and compiling
    player._poll, player._track, player._known = "poll", "track", None
    return player


ID = "spotify:track:5gmdnWypZHZ9fzBpnLjdHX"
DETAILS = a_list(
    text(ID),
    text("Alışamadım"),
    text("Ezhel"),
    NSAppleEventDescriptor.descriptorWithInt32_(153268),
)


def test_a_reading_with_details(monkeypatch: pytest.MonkeyPatch) -> None:
    replies = Replies(a_list(text(ID), text("playing"), real(42.7505)), DETAILS)
    player = turntable(monkeypatch, replies)
    expected = Reading(ID, "Alışamadım", "Ezhel", 153268, True, 42751)
    assert player.read() == expected
    assert player.read() == expected
    assert replies.track_calls == 1  # details are asked once per track


def test_an_unknown_position_stays_unknown(monkeypatch: pytest.MonkeyPatch) -> None:
    player = turntable(
        monkeypatch, Replies(a_list(text(ID), text("paused"), missing()), DETAILS)
    )
    reading = player.read()
    assert reading is not None and reading.position_ms is None and not reading.playing


def test_an_ad_is_no_track(monkeypatch: pytest.MonkeyPatch) -> None:
    poll = a_list(text("spotify:ad:123"), text("playing"), real(5.0))
    replies = Replies(poll, DETAILS)
    assert turntable(monkeypatch, replies).read() == Reading(
        None, None, None, None, True, None
    )
    assert replies.track_calls == 0


def test_nothing_loaded_is_no_track(monkeypatch: pytest.MonkeyPatch) -> None:
    poll = a_list(missing(), text("stopped"), real(0.0))
    reading = turntable(monkeypatch, Replies(poll)).read()
    assert reading == Reading(None, None, None, None, False, None)


@pytest.mark.parametrize("reply", [text("not running"), None])
def test_closed_or_silent_spotify_reads_none(
    monkeypatch: pytest.MonkeyPatch, reply: object
) -> None:
    assert turntable(monkeypatch, Replies(reply)).read() is None


def test_details_of_a_track_that_changed_meanwhile_are_not_used(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    other = a_list(text("spotify:track:other"), text("X"), text("Y"), real(1.0))
    player = turntable(
        monkeypatch, Replies(a_list(text(ID), text("playing"), real(1.0)), other)
    )
    reading = player.read()
    assert reading is not None and (reading.title, reading.duration_ms) == (None, None)


def test_place_quotes_the_track_and_writes_seconds_with_a_dot(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    sources = []
    monkeypatch.setattr(
        spotify, "_compile", lambda source: sources.append(source) or source
    )
    monkeypatch.setattr(
        spotify,
        "_run",
        lambda script: NSAppleEventDescriptor.descriptorWithBoolean_(True),
    )
    player = Spotify.__new__(Spotify)
    assert player.place('spotify:track:a"b\\c', 103500) is True
    assert '"spotify:track:a\\"b\\\\c"' in sources[0]
    assert "set player position to 103.500" in sources[0]


class FailingScript:
    def __init__(self, number: int) -> None:
        self.number = number

    def executeAndReturnError_(self, _: object) -> tuple[None, dict]:
        return None, {"NSAppleScriptErrorNumber": self.number}


def test_a_missing_permission_is_said_plainly() -> None:
    with pytest.raises(PermissionError, match="Automation"):
        spotify._run(FailingScript(-1743))


@pytest.mark.parametrize("number", [-600, -609, -1712])
def test_a_spotify_that_quit_or_hangs_gives_no_reading(number: int) -> None:
    assert spotify._run(FailingScript(number)) is None


def test_other_errors_are_not_hidden() -> None:
    with pytest.raises(RuntimeError, match="did not answer"):
        spotify._run(FailingScript(-10000))
