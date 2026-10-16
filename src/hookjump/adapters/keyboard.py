"""What a key combination types on the listener's keyboard layout right now.

A shortcut recorder needs it: on a Turkish Q keyboard ⌥Q types "@", and taking
⌥Q as a hotkey would leave the listener unable to type "@". Asked of macOS
(UCKeyTranslate), so it is right for any layout, not a table we keep.

Known limit: a letter typed with ⌥ counts as rare, like ⌥F's "ƒ". On layouts
that type their own letters with ⌥ (Polish Pro: ⌥A is "ą"), such a shortcut
would still be accepted and take that letter away.
"""

import ctypes
import ctypes.util

from hookjump.shortcut import Shortcut

_SHIFT_OPTION = 0x0200 | 0x0800  # the only modifiers that change typed text
_COMMAND_CONTROL = 0x0100 | 0x1000
_KEY_DOWN = 0  # kUCKeyActionDown


def typed_by(shortcut: Shortcut) -> tuple[str | None, bool]:
    """(the text it types, or None; whether it starts an accent instead)."""
    if shortcut.modifiers & _COMMAND_CONTROL:
        return None, False  # ⌘ and ⌃ combinations type nothing
    carbon = ctypes.cdll.LoadLibrary(ctypes.util.find_library("Carbon"))
    foundation = ctypes.cdll.LoadLibrary(ctypes.util.find_library("CoreFoundation"))
    carbon.TISCopyCurrentKeyboardLayoutInputSource.restype = ctypes.c_void_p
    carbon.TISGetInputSourceProperty.restype = ctypes.c_void_p
    carbon.TISGetInputSourceProperty.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    carbon.LMGetKbdType.restype = ctypes.c_uint8
    foundation.CFDataGetBytePtr.restype = ctypes.c_void_p
    foundation.CFDataGetBytePtr.argtypes = [ctypes.c_void_p]
    foundation.CFRelease.argtypes = [ctypes.c_void_p]

    source = carbon.TISCopyCurrentKeyboardLayoutInputSource()
    try:
        key = ctypes.c_void_p.in_dll(carbon, "kTISPropertyUnicodeKeyLayoutData")
        data = carbon.TISGetInputSourceProperty(source, key)
        if not data:
            return None, False  # an input method without a key layout
        layout = foundation.CFDataGetBytePtr(data)
        dead, length, chars = (
            ctypes.c_uint32(0),
            ctypes.c_ulong(0),
            (ctypes.c_uint16 * 4)(),
        )
        carbon.UCKeyTranslate(
            ctypes.c_void_p(layout),
            ctypes.c_uint16(shortcut.key_code),
            ctypes.c_uint16(_KEY_DOWN),
            ctypes.c_uint32((shortcut.modifiers & _SHIFT_OPTION) >> 8),
            ctypes.c_uint32(carbon.LMGetKbdType()),
            ctypes.c_uint32(0),  # dead keys on: an accent key reports itself
            ctypes.byref(dead),
            ctypes.c_ulong(len(chars)),
            ctypes.byref(length),
            chars,
        )
    finally:
        foundation.CFRelease(source)
    if length.value == 0 and dead.value:
        return None, True
    text = "".join(chr(chars[i]) for i in range(length.value))
    return text or None, False
