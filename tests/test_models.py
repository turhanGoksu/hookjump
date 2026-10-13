"""A jump result has a target exactly when its status says it should."""

import pytest

from hookjump.core.models import JumpResult, Status

TARGET_STATUSES = [s for s in Status if s.has_target]
NO_TARGET_STATUSES = [s for s in Status if not s.has_target]


def test_status_values_match_spec() -> None:
    # spec/ALGORITHM.md section 2 and every JSON vector use these strings.
    assert {s.value for s in Status} == {
        "pinned",
        "personal",
        "chorus",
        "no_data",
        "no_lyrics",
        "instrumental",
        "not_found",
        "player_unavailable",
    }
    assert {s.value for s in TARGET_STATUSES} == {"pinned", "personal", "chorus"}


@pytest.mark.parametrize("status", TARGET_STATUSES)
def test_target_status_needs_a_target(status: Status) -> None:
    assert JumpResult(status, 60500).target_ms == 60500
    with pytest.raises(ValueError, match="needs a target_ms"):
        JumpResult(status)


@pytest.mark.parametrize("status", NO_TARGET_STATUSES)
def test_no_target_status_refuses_a_target(status: Status) -> None:
    assert JumpResult(status).target_ms is None
    with pytest.raises(ValueError, match="must not carry"):
        JumpResult(status, 60500)


@pytest.mark.parametrize("bad", [60.5, 60500.0, True])
def test_target_must_be_int_milliseconds(bad: object) -> None:
    with pytest.raises(TypeError, match="int milliseconds"):
        JumpResult(Status.CHORUS, bad)  # type: ignore[arg-type]


def test_target_cannot_be_negative() -> None:
    with pytest.raises(ValueError, match=">= 0"):
        JumpResult(Status.CHORUS, -1)
    assert JumpResult(Status.CHORUS, 0).target_ms == 0
