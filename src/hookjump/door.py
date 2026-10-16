"""Door 2: the ``hookjump jump`` / ``pin`` commands knock on the running app.

The knock is a macOS distributed notification: no permission, nothing to set
up, and it works whether or not Carbon hotkeys do. Any tool can use it, such
as a keyboard shortcut made in the Shortcuts app. Since any program on the Mac
could knock, only harmless requests get in: jump, pin, and "are you there?".
Nothing that deletes or changes settings ever goes through this door.
"""

import uuid
from collections.abc import Callable

from Foundation import (
    NSDate,
    NSDistributedNotificationCenter,
    NSObject,
    NSRunLoop,
)

# The notification names are "<channel>.request" and "<channel>.reply". Tests
# use channels of their own, so they can never reach a hookjump that is running.
CHANNEL = "app.hookjump"
ACTIONS = frozenset({"jump", "pin", "ping"})
_DELIVER_IMMEDIATELY = 4  # NSNotificationSuspensionBehaviorDeliverImmediately


def _center():  # -> NSDistributedNotificationCenter
    return NSDistributedNotificationCenter.defaultCenter()


class _Observer(NSObject):
    """An Objective-C object that forwards notifications to a Python function."""

    def initWithHandler_(self, handler):  # noqa: N802 (Objective-C naming)
        self = self.init()
        self._handler = handler
        return self

    def receive_(self, note):  # noqa: N802
        info = note.userInfo() or {}
        self._handler({str(k): info[k] for k in info})


def knock(action: str, timeout_s: float = 2.0, channel: str = CHANNEL) -> dict | None:
    """Ask the running app for ``action``; its reply, or None if nobody answers."""
    if action not in ACTIONS:
        raise ValueError(f"the door only takes {sorted(ACTIONS)}")
    request_id = str(uuid.uuid4())
    replies: list[dict] = []

    def on_reply(info: dict) -> None:
        if info.get("id") == request_id:
            replies.append(info)

    observer = _Observer.alloc().initWithHandler_(on_reply)
    _center().addObserver_selector_name_object_(
        observer, "receive:", f"{channel}.reply", None
    )
    try:
        _center().postNotificationName_object_userInfo_deliverImmediately_(
            f"{channel}.request", None, {"action": action, "id": request_id}, True
        )
        deadline = NSDate.dateWithTimeIntervalSinceNow_(timeout_s)
        while not replies and NSDate.date().compare_(deadline) < 0:
            NSRunLoop.currentRunLoop().runUntilDate_(
                NSDate.dateWithTimeIntervalSinceNow_(0.05)
            )
    finally:
        _center().removeObserver_(observer)
    return replies[0] if replies else None


class DoorKeeper:
    """The app's side of the door: lets in the allowed requests and answers."""

    def __init__(self, handle: Callable[[str], dict], channel: str = CHANNEL) -> None:
        self._handle = handle
        self._channel = channel
        self._observer = _Observer.alloc().initWithHandler_(self._on_request)
        _center().addObserver_selector_name_object_suspensionBehavior_(
            self._observer, "receive:", f"{channel}.request", None, _DELIVER_IMMEDIATELY
        )

    def close(self) -> None:
        _center().removeObserver_(self._observer)

    def _on_request(self, info: dict) -> None:
        action, request_id = info.get("action"), info.get("id")
        if action not in ACTIONS or not isinstance(request_id, str):
            return  # not ours, or not allowed: no answer at all
        reply = {"ok": True} if action == "ping" else self._handle(action)
        # A notification carries property-list values only: no None.
        info = {k: v for k, v in reply.items() if v is not None}
        _center().postNotificationName_object_userInfo_deliverImmediately_(
            f"{self._channel}.reply", None, {**info, "id": request_id}, True
        )
