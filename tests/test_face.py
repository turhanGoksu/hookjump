"""The menu bar face: the hook's mood and the line shown after a hotkey."""

import pytest

from hookjump.core.models import JumpResult, Status
from hookjump.face import Frame, Mood, jump_frames, line, mood, pin_frames
from hookjump.playback import Landing

MINUTE = 60_000
SONG = 4 * MINUTE  # 11 cells of about 21.8 s


@pytest.mark.parametrize(
    ("playing", "paused", "known", "expected"),
    [
        (True, False, True, Mood.KNOWN),
        (True, False, False, Mood.UNKNOWN),
        (True, True, True, Mood.RESTING),
        (False, False, True, Mood.RESTING),
    ],
)
def test_mood(playing: bool, paused: bool, known: bool, expected: Mood) -> None:
    assert (
        mood(playing_track=playing, learning_paused=paused, spot_known=known)
        == expected
    )


def test_line_shows_where_playback_is_and_where_the_mark_is() -> None:
    assert line(MINUTE, SONG, 3 * MINUTE) == "━━●━━━━━◆━━"


def test_line_merges_both_in_one_cell() -> None:
    assert line(3 * MINUTE, SONG, 3 * MINUTE + 5000) == "━━━━━━━━◉━━"


def test_line_without_a_mark_ends_in_a_cross() -> None:
    assert line(MINUTE, SONG, None) == "━━●━━━━━━━✕"


def test_line_ends_stay_inside() -> None:
    assert line(0, SONG, SONG) == "●━━━━━━━━━◆"
    assert line(SONG + 9000, SONG, None) == "━━━━━━━━━━●"  # playback wins the cell


def test_no_line_without_a_duration() -> None:
    assert line(MINUTE, None, MINUTE) == ""
    assert line(MINUTE, 0, MINUTE) == ""


def test_no_position_draws_only_the_mark() -> None:
    assert line(None, SONG, 3 * MINUTE) == "━━━━━━━━◆━━"


def test_a_landed_jump_shows_the_move_then_closes() -> None:
    frames = jump_frames(
        MINUTE, SONG, JumpResult(Status.PERSONAL, 3 * MINUTE), Landing.LANDED
    )
    assert frames == [
        Frame("━━●━━━━━◆━━", 0.8),
        Frame("━━━━━━━━◉━━", 2.2),
        Frame("", 0),
    ]


def test_a_slipped_jump_keeps_the_mark_unreached() -> None:
    frames = jump_frames(
        MINUTE, SONG, JumpResult(Status.CHORUS, 3 * MINUTE), Landing.SLIPPED
    )
    assert frames[1] == Frame("━━●━━━━━◆━━ ✕", 2.2)


def test_no_target_shows_the_cross_for_the_whole_time() -> None:
    frames = jump_frames(MINUTE, SONG, JumpResult(Status.NO_DATA), None)
    assert frames == [Frame("━━●━━━━━━━✕", 3.0), Frame("", 0)]


def test_pinning_moves_the_mark_to_playback() -> None:
    frames = pin_frames(2 * MINUTE, SONG, 3 * MINUTE)
    assert [f.text for f in frames] == ["━━━━━●━━◆━━", "━━━━━◉━━━━━", ""]
