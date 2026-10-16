"""Shortcuts as data: parsing, showing and storing them."""

import pytest

from hookjump.shortcut import JUMP_DEFAULT, PIN_DEFAULT, Shortcut


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
