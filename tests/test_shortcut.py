"""Shortcuts as data: parsing, showing and storing them."""

import pytest

from hookjump.shortcut import JUMP_DEFAULT, PIN_DEFAULT, Shortcut, why_not


def test_the_defaults() -> None:
    assert str(Shortcut.parse(JUMP_DEFAULT)) == "⌥F"
    assert str(Shortcut.parse(PIN_DEFAULT)) == "⌥D"


@pytest.mark.parametrize(
    ("text", "shown"),
    [
        ("ctrl+cmd+p", "⌃⌘P"),
        ("CMD + Ctrl + P", "⌃⌘P"),  # case, spaces and order do not matter
        ("control+option+command+j", "⌃⌥⌘J"),
        ("alt+shift+right", "⌥⇧→"),
        ("cmd+space", "⌘Space"),
        ("ctrl+f5", "⌃F5"),
        ("opt+3", "⌥3"),
    ],
)
def test_parse_and_show(text: str, shown: str) -> None:
    assert str(Shortcut.parse(text)) == shown


def test_codes_match_carbon() -> None:
    shortcut = Shortcut.parse("opt+j")
    assert (shortcut.key_code, shortcut.modifiers) == (0x26, 0x0800)


@pytest.mark.parametrize("text", ["opt+j", "ctrl+opt+shift+cmd+up", "cmd+0"])
def test_stored_as_one_integer(text: str) -> None:
    shortcut = Shortcut.parse(text)
    assert Shortcut.from_int(shortcut.to_int()) == shortcut
    assert shortcut.to_int() > 0  # 0 means "not set" in the settings table


@pytest.mark.parametrize(
    ("text", "error"),
    [
        ("j", "needs ctrl, opt or cmd"),
        ("shift+j", "needs ctrl, opt or cmd"),
        ("opt+ğ", "unknown key"),
        ("hyper+j", "unknown modifier"),
    ],
)
def test_refused(text: str, error: str) -> None:
    with pytest.raises(ValueError, match=error):
        Shortcut.parse(text)


CMD, SHIFT, OPT, CTRL = 1 << 20, 1 << 17, 1 << 19, 1 << 18  # AppKit flags


@pytest.mark.parametrize(
    ("key_code", "flags", "shown"),
    [(0x03, OPT, "⌥F"), (0x23, CTRL | CMD, "⌃⌘P"), (0x7C, OPT | SHIFT, "⌥⇧→"),
     (0x26, OPT | 0xFF, "⌥J")],  # other flag bits (caps lock...) are ignored
)  # fmt: skip
def test_from_a_key_press(key_code: int, flags: int, shown: str) -> None:
    assert str(Shortcut.from_cocoa(key_code, flags)) == shown


@pytest.mark.parametrize(
    ("key_code", "flags", "error"),
    [(0x03, 0, "hold"), (0x03, SHIFT, "hold"), (0x30, OPT, "use a letter")],
)  # 0x30 is Tab
def test_key_presses_refused(key_code: int, flags: int, error: str) -> None:
    with pytest.raises(ValueError, match=error):
        Shortcut.from_cocoa(key_code, flags)


@pytest.mark.parametrize(
    ("text", "typed", "dead", "reason"),
    [
        ("cmd+c", None, False, "⌘C is Copy in every app."),
        ("shift+cmd+4", None, False, "⇧⌘4 is a screenshot in every app."),
        ("ctrl+r", None, False, "⌃R is history search (in Terminal) in every app."),
        ("opt+q", "@", False, "On your keyboard ⌥Q types “@”."),
        ("opt+t", "₺", False, "On your keyboard ⌥T types “₺”."),
        ("opt+e", "", True, "On your keyboard ⌥E starts an accent, as in é."),
    ],
)
def test_harmful_shortcuts_are_refused(
    text: str, typed: str | None, dead: bool, reason: str
) -> None:
    assert why_not(Shortcut.parse(text), typed, dead) == reason


@pytest.mark.parametrize(
    ("text", "typed"),
    [("opt+f", "ƒ"), ("opt+d", "∂"), ("opt+j", "∆"), ("ctrl+cmd+j", None),
     ("ctrl+opt+cmd+p", None)],
)  # fmt: skip
def test_safe_shortcuts_pass(text: str, typed: str | None) -> None:
    assert why_not(Shortcut.parse(text), typed, False) is None
