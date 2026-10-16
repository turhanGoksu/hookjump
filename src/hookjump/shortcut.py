"""Keyboard shortcuts as data: "opt+j" in, ⌥J out, one integer in storage.

Keys are named by their place on a US keyboard, as macOS key codes are: on a
Turkish Q keyboard, "j" is the same key. Registering them is the job of an
adapter (hookjump.adapters.carbon_hotkeys).
"""

from dataclasses import dataclass

JUMP_DEFAULT = "opt+f"  # F: go to the favorite spot
PIN_DEFAULT = "opt+d"  # D: drop anchor here (Turkish "demir at")

# macOS virtual key codes (Carbon's kVK_* constants) by key name.
_KEYS = {
    "a": 0x00, "s": 0x01, "d": 0x02, "f": 0x03, "h": 0x04, "g": 0x05, "z": 0x06,
    "x": 0x07, "c": 0x08, "v": 0x09, "b": 0x0B, "q": 0x0C, "w": 0x0D, "e": 0x0E,
    "r": 0x0F, "y": 0x10, "t": 0x11, "1": 0x12, "2": 0x13, "3": 0x14, "4": 0x15,
    "6": 0x16, "5": 0x17, "9": 0x19, "7": 0x1A, "8": 0x1C, "0": 0x1D, "o": 0x1F,
    "u": 0x20, "i": 0x22, "p": 0x23, "l": 0x25, "j": 0x26, "k": 0x28, "n": 0x2D,
    "m": 0x2E, "space": 0x31, "f1": 0x7A, "f2": 0x78, "f3": 0x63, "f4": 0x76,
    "f5": 0x60, "f6": 0x61, "f7": 0x62, "f8": 0x64, "f9": 0x65, "f10": 0x6D,
    "f11": 0x67, "f12": 0x6F, "left": 0x7B, "right": 0x7C, "down": 0x7D,
    "up": 0x7E,
}  # fmt: skip
_NAMES = {code: name for name, code in _KEYS.items()}
_SHOWN = {"space": "Space", "left": "←", "right": "→", "down": "↓", "up": "↑"}

# Carbon modifier masks, in the order macOS shows them: ⌃⌥⇧⌘.
_MODIFIERS = {"ctrl": 0x1000, "opt": 0x0800, "shift": 0x0200, "cmd": 0x0100}
_SYMBOLS = {"ctrl": "⌃", "opt": "⌥", "shift": "⇧", "cmd": "⌘"}
_ALIASES = {"control": "ctrl", "option": "opt", "alt": "opt", "command": "cmd"}


@dataclass(frozen=True)
class Shortcut:
    key_code: int
    modifiers: int  # a Carbon modifier mask

    @classmethod
    def parse(cls, text: str) -> "Shortcut":
        """``"opt+j"``, ``"ctrl+cmd+p"``; case and spaces do not matter."""
        parts = [p.strip().lower() for p in text.split("+")]
        key = parts[-1]
        if key not in _KEYS:
            raise ValueError(f"unknown key {key!r} in {text!r}")
        mask = 0
        for part in parts[:-1]:
            name = _ALIASES.get(part, part)
            if name not in _MODIFIERS:
                raise ValueError(f"unknown modifier {part!r} in {text!r}")
            mask |= _MODIFIERS[name]
        # Shift alone, or no modifier, would take a key from every text field.
        if not mask & (_MODIFIERS["ctrl"] | _MODIFIERS["opt"] | _MODIFIERS["cmd"]):
            raise ValueError(f"{text!r} needs ctrl, opt or cmd")
        return cls(_KEYS[key], mask)

    def __str__(self) -> str:
        symbols = "".join(
            s for m, s in _SYMBOLS.items() if self.modifiers & _MODIFIERS[m]
        )
        name = _NAMES[self.key_code]
        return symbols + _SHOWN.get(name, name.upper())

    def to_int(self) -> int:
        """One integer, for the settings table."""
        return self.key_code << 16 | self.modifiers

    @classmethod
    def from_int(cls, value: int) -> "Shortcut":
        return cls(value >> 16, value & 0xFFFF)

    @classmethod
    def from_cocoa(cls, key_code: int, flags: int) -> "Shortcut":
        """From a key press as AppKit reports it (``NSEvent`` key code and flags)."""
        if key_code not in _NAMES:
            raise ValueError("use a letter, a digit, F1-F12, an arrow or Space")
        mask = 0
        for bit, name in _COCOA_FLAGS.items():
            if flags & bit:
                mask |= _MODIFIERS[name]
        if not mask & (_MODIFIERS["ctrl"] | _MODIFIERS["opt"] | _MODIFIERS["cmd"]):
            raise ValueError("hold ⌃, ⌥ or ⌘ with the key")
        return cls(key_code, mask)


_COCOA_FLAGS = {1 << 17: "shift", 1 << 18: "ctrl", 1 << 19: "opt", 1 << 20: "cmd"}

# Shortcuts every app or the Terminal relies on: taking one breaks it everywhere.
_RESERVED_TEXT = {
    "cmd+c": "Copy", "cmd+v": "Paste", "cmd+x": "Cut", "cmd+z": "Undo",
    "shift+cmd+z": "Redo", "cmd+a": "Select All", "cmd+s": "Save",
    "cmd+q": "Quit", "cmd+w": "Close", "cmd+h": "Hide", "cmd+m": "Minimize",
    "cmd+f": "Find", "cmd+p": "Print", "cmd+n": "New", "cmd+t": "New Tab",
    "cmd+o": "Open", "cmd+r": "Reload", "cmd+space": "Spotlight",
    "ctrl+cmd+space": "the emoji picker", "ctrl+cmd+q": "Lock Screen",
    "ctrl+cmd+f": "Full Screen", "shift+cmd+3": "a screenshot",
    "shift+cmd+4": "a screenshot", "shift+cmd+5": "a screenshot",
    "ctrl+c": "stop (in Terminal)", "ctrl+d": "end of input (in Terminal)",
    "ctrl+z": "suspend (in Terminal)", "ctrl+a": "line start (in Terminal)",
    "ctrl+e": "line end (in Terminal)", "ctrl+k": "delete to line end",
    "ctrl+r": "history search (in Terminal)", "ctrl+u": "delete the line",
    "ctrl+w": "delete a word", "ctrl+l": "clear (in Terminal)",
}  # fmt: skip
_RESERVED = {Shortcut.parse(text): use for text, use in _RESERVED_TEXT.items()}
_EVERYDAY = set("€₺£¥¢")  # besides ASCII


def why_not(shortcut: Shortcut, typed: str | None, dead_key: bool) -> str | None:
    """Why this shortcut would break something, or None if it is safe to take.

    ``typed`` is what the key combination types on the listener's keyboard
    layout (None if nothing), and ``dead_key`` whether it starts an accent:
    both come from an adapter that asks macOS.
    """
    if shortcut in _RESERVED:
        return f"{shortcut} is {_RESERVED[shortcut]} in every app."
    if dead_key:
        return f"On your keyboard {shortcut} starts an accent, as in é."
    if typed and all(" " <= c <= "~" or c in _EVERYDAY for c in typed):
        return f"On your keyboard {shortcut} types “{typed}”."
    return None
