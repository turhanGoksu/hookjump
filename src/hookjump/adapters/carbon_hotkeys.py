"""Door 1: global hotkeys through Carbon's RegisterEventHotKey, via ctypes.

Every shortcut manager on macOS uses this call. It needs no permission (an
event monitor would need Accessibility), and it takes the key: the app in
front does not also get it. Carbon is old, so this is the only part that
depends on it, and door 2 (the ``hookjump jump`` command) works without it.

The callbacks run on the main thread, while the app's run loop is running.
"""

import ctypes
import ctypes.util
from collections.abc import Callable

from hookjump.shortcut import Shortcut


def _code(text: str) -> int:
    return int.from_bytes(text.encode("latin-1"), "big")


class _HotKeyID(ctypes.Structure):
    _fields_ = [("signature", ctypes.c_uint32), ("id", ctypes.c_uint32)]


class _EventTypeSpec(ctypes.Structure):
    _fields_ = [("eventClass", ctypes.c_uint32), ("eventKind", ctypes.c_uint32)]


_HANDLER = ctypes.CFUNCTYPE(
    ctypes.c_int32, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p
)
_SIGNATURE = _code("hkjp")
_HOT_KEY_PRESSED = 5  # kEventHotKeyPressed
_NOT_HANDLED = -9874  # eventNotHandledErr


class HotkeyError(RuntimeError):
    """macOS refused a shortcut (15.0 and 15.1 refuse ones with ⌥ alone).

    A shortcut another app has registered is not refused: macOS lets two apps
    register the same one, so such a clash cannot be found out here.
    """


class CarbonHotkeys:
    def __init__(self) -> None:
        self._carbon = ctypes.cdll.LoadLibrary(ctypes.util.find_library("Carbon"))
        c = self._carbon
        c.GetApplicationEventTarget.restype = ctypes.c_void_p
        c.InstallEventHandler.argtypes = [
            ctypes.c_void_p, _HANDLER, ctypes.c_ulong,
            ctypes.POINTER(_EventTypeSpec), ctypes.c_void_p,
            ctypes.POINTER(ctypes.c_void_p),
        ]  # fmt: skip
        c.RegisterEventHotKey.argtypes = [
            ctypes.c_uint32, ctypes.c_uint32, _HotKeyID, ctypes.c_void_p,
            ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p),
        ]  # fmt: skip
        c.UnregisterEventHotKey.argtypes = [ctypes.c_void_p]
        c.GetEventParameter.argtypes = [
            ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p,
            ctypes.c_ulong, ctypes.c_void_p, ctypes.c_void_p,
        ]  # fmt: skip

        self._actions: dict[int, Callable[[], None]] = {}
        self._refs: dict[int, ctypes.c_void_p] = {}
        # Keep the C callback alive as long as this object: if Python freed it,
        # the next key press would call freed memory and crash the app.
        self._callback = _HANDLER(self._on_event)
        spec = _EventTypeSpec(_code("keyb"), _HOT_KEY_PRESSED)
        status = c.InstallEventHandler(
            c.GetApplicationEventTarget(), self._callback, 1, ctypes.byref(spec),
            None, ctypes.byref(ctypes.c_void_p()),
        )  # fmt: skip
        if status != 0:
            raise HotkeyError(f"could not listen for hotkeys (error {status})")

    def bind(self, shortcut: Shortcut, action: Callable[[], None]) -> None:
        number = len(self._actions) + 1
        ref = ctypes.c_void_p()
        status = self._carbon.RegisterEventHotKey(
            shortcut.key_code, shortcut.modifiers, _HotKeyID(_SIGNATURE, number),
            self._carbon.GetApplicationEventTarget(), 0, ctypes.byref(ref),
        )  # fmt: skip
        if status != 0:
            raise HotkeyError(f"{shortcut} could not be registered (error {status})")
        self._actions[number] = action
        self._refs[number] = ref

    def unbind_all(self) -> None:
        for ref in self._refs.values():
            self._carbon.UnregisterEventHotKey(ref)
        self._refs.clear()
        self._actions.clear()

    def _on_event(self, _call: object, event: object, _data: object) -> int:
        # Never let an exception cross into C: report it and carry on.
        try:
            hot_key = _HotKeyID()
            self._carbon.GetEventParameter(
                event, _code("----"), _code("hkid"), None,
                ctypes.sizeof(hot_key), None, ctypes.byref(hot_key),
            )  # fmt: skip
            action = self._actions.get(hot_key.id)
            if hot_key.signature != _SIGNATURE or action is None:
                return _NOT_HANDLED
            action()
        except Exception:  # noqa: BLE001
            import traceback

            traceback.print_exc()
        return 0
