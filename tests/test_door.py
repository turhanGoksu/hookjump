"""Door 2 round trips inside one process. macOS only (distributed notifications)."""

import uuid

import pytest

pytest.importorskip("Foundation")

from Foundation import NSDistributedNotificationCenter  # noqa: E402

from hookjump.door import DoorKeeper  # noqa: E402
from hookjump.door import knock as real_knock  # noqa: E402

# A channel of its own: these tests must never knock on a hookjump that the
# developer has running (an early version did, and made Spotify jump).
CHANNEL = f"test.hookjump.{uuid.uuid4()}"


def knock(action: str, timeout_s: float = 2.0) -> dict | None:
    return real_knock(action, timeout_s, channel=CHANNEL)


@pytest.fixture
def keeper():
    asked = []

    def handle(action: str) -> dict:
        asked.append(action)
        return {"status": "no_data", "target_ms": None}  # None must not break it

    keeper = DoorKeeper(handle, channel=CHANNEL)
    keeper.asked = asked
    yield keeper
    keeper.close()


def test_a_jump_is_let_in_and_answered(keeper) -> None:
    reply = knock("jump")
    assert reply is not None and reply["status"] == "no_data"
    assert "target_ms" not in reply  # dropped, as notifications carry no None
    assert keeper.asked == ["jump"]


def test_ping_answers_without_doing_anything(keeper) -> None:
    assert knock("ping")["ok"]
    assert keeper.asked == []


def test_nobody_home(keeper) -> None:
    keeper.close()
    assert knock("jump", timeout_s=0.3) is None


def test_the_command_side_only_sends_allowed_requests() -> None:
    with pytest.raises(ValueError, match="only takes"):
        knock("forget_all")


def test_the_default_channel_is_the_apps() -> None:
    assert real_knock.__defaults__[-1] == "app.hookjump"


def test_the_app_side_ignores_anything_else(keeper) -> None:
    # Another program on the Mac knocks with a request that is not allowed.
    center = NSDistributedNotificationCenter.defaultCenter()
    center.postNotificationName_object_userInfo_deliverImmediately_(
        f"{CHANNEL}.request", None, {"action": "forget_all", "id": "x"}, True
    )
    assert knock("ping") is not None  # the door still works...
    assert keeper.asked == []  # ...and the bad request reached nothing
