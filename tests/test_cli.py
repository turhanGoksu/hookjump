"""The hookjump command, with the door replaced by a stand-in."""

import pytest

from hookjump.cli import main


@pytest.fixture(autouse=True)
def on_macos(monkeypatch: pytest.MonkeyPatch) -> None:
    # The command refuses other systems; these tests also run on Linux in CI.
    monkeypatch.setattr("sys.platform", "darwin")


def answering(reply: dict | None):
    asked = []

    def knock(action: str) -> dict | None:
        asked.append(action)
        return reply

    return knock, asked


def test_jump_tells_where_it_landed(capsys: pytest.CaptureFixture[str]) -> None:
    knock, asked = answering(
        {"status": "personal", "target_ms": 103500, "landing": "landed"}
    )
    assert main(["jump"], knock) == 0
    assert asked == ["jump"]
    assert capsys.readouterr().out == "Jumped to 1:43: the part you replay most.\n"


def test_no_jump_is_an_error_exit(capsys: pytest.CaptureFixture[str]) -> None:
    knock, _ = answering({"status": "no_data"})
    assert main(["jump"], knock) == 1
    assert "No jump: nothing pinned or learned" in capsys.readouterr().out


def test_pin(capsys: pytest.CaptureFixture[str]) -> None:
    knock, asked = answering({"pinned_ms": 72000})
    assert main(["pin"], knock) == 0
    assert asked == ["pin"]
    assert "Pinned 1:12" in capsys.readouterr().out


def test_nobody_answers(capsys: pytest.CaptureFixture[str]) -> None:
    knock, _ = answering(None)
    assert main(["jump"], knock) == 1
    assert "hookjump is not running" in capsys.readouterr().err


def test_version(capsys: pytest.CaptureFixture[str]) -> None:
    with pytest.raises(SystemExit) as exit_info:
        main(["--version"])
    assert exit_info.value.code == 0
    assert capsys.readouterr().out.startswith("hookjump ")


def test_other_systems_get_a_plain_answer(
    capsys: pytest.CaptureFixture[str], monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr("sys.platform", "linux")
    knock, asked = answering({"status": "personal"})
    assert main(["jump"], knock) == 1
    assert asked == []  # nothing was tried
    assert capsys.readouterr().err == "hookjump runs on macOS only, for now.\n"
