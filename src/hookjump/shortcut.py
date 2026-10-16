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
