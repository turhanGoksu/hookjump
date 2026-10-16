"""The shortcut recorder: press a combination, see it, and see why if it is refused.

It refuses what would break something (hookjump.shortcut.why_not): a
combination that types an everyday character on this keyboard, starts an
accent, or that every app or the Terminal uses. The hotkeys must be released
while it is open, or pressing the old one would jump instead of being recorded.
"""

from collections.abc import Callable

import objc
from AppKit import (
    NSAlert,
    NSApplication,
    NSColor,
    NSFont,
    NSFontAttributeName,
    NSForegroundColorAttributeName,
    NSMakeRect,
    NSView,
)
from Foundation import NSMakePoint, NSString

from hookjump.adapters.keyboard import typed_by
from hookjump.shortcut import Shortcut, why_not

_COMMAND_OR_CONTROL = (1 << 20) | (1 << 18)  # AppKit modifier flags
_ESCAPE = 53
_FIRST_BUTTON = 1000  # NSAlertFirstButtonReturn
_WIDTH, _HEIGHT = 300.0, 66.0


class KeyCatcher(NSView):
    """Takes the next key combination pressed while it has the focus."""

    def initWithFrame_(self, frame):  # noqa: N802
        self = objc.super(KeyCatcher, self).initWithFrame_(frame)
        if self is None:
            return None
        self.shortcut = None  # the accepted combination, if any
        self.pressed = None  # what was pressed last, shown even if refused
        self.reason = None
        self.taken: Shortcut | None = None  # the other hotkey: not twice
        self.on_change: Callable[[bool], None] | None = None
        return self

    def acceptsFirstResponder(self):  # noqa: N802
        return True

    def isFlipped(self):  # noqa: N802
        return True

    def keyDown_(self, event):  # noqa: N802
        if event.keyCode() == _ESCAPE:  # leave Escape to the Cancel button
            objc.super(KeyCatcher, self).keyDown_(event)
            return
        self._take(event)

    def performKeyEquivalent_(self, event):  # noqa: N802
        # ⌘ and ⌃ combinations come here first; take them before the alert's
        # buttons or the menus do.
        if event.modifierFlags() & _COMMAND_OR_CONTROL:
            self._take(event)
            return True
        return False

    @objc.python_method
    def _take(self, event) -> None:
        self.shortcut = None
        try:
            pressed = Shortcut.from_cocoa(event.keyCode(), event.modifierFlags())
        except ValueError as error:
            self.pressed, self.reason = None, f"{str(error).capitalize()}."
        else:
            typed, dead_key = typed_by(pressed)
            self.pressed = pressed
            self.reason = why_not(pressed, typed, dead_key)
            if self.reason is None and pressed == self.taken:
                self.reason = f"{pressed} is already the other hookjump shortcut."
            if self.reason is None:
                self.shortcut = pressed
        if self.on_change:
            self.on_change(self.shortcut is not None)
        self.setNeedsDisplay_(True)

    def drawRect_(self, _rect):  # noqa: N802
        if self.pressed is None and self.reason is None:
            self._text("Press the new shortcut…", 16, NSColor.secondaryLabelColor(), 18)
            return
        if self.pressed is not None:
            self._text(str(self.pressed), 26, NSColor.labelColor(), 4)
        if self.reason:
            self._text(f"✕ {self.reason}", 12, NSColor.systemRedColor(), 42)
        else:
            self._text("✓ Nothing on your keyboard needs it.", 12,
                       NSColor.secondaryLabelColor(), 42)  # fmt: skip

    @objc.python_method
    def _text(self, text: str, size: float, color, y: float) -> None:
        attributes = {
            NSFontAttributeName: NSFont.systemFontOfSize_(size),
            NSForegroundColorAttributeName: color,
        }
        string = NSString.stringWithString_(text)
        width = string.sizeWithAttributes_(attributes).width
        string.drawAtPoint_withAttributes_(
            NSMakePoint((_WIDTH - width) / 2, y), attributes
        )


def record(what: str, current: Shortcut, taken: Shortcut) -> Shortcut | None:
    """Ask for a new shortcut for ``what``; None if the listener cancels."""
    alert = NSAlert.alloc().init()
    alert.setMessageText_(f"New shortcut to {what}")
    alert.setInformativeText_(
        f"Now it is {current}. Combinations that would stop you typing a"
        " character, or that every app uses, are not accepted."
    )
    save = alert.addButtonWithTitle_("Save")
    alert.addButtonWithTitle_("Cancel")
    save.setEnabled_(False)
    catcher = KeyCatcher.alloc().initWithFrame_(NSMakeRect(0, 0, _WIDTH, _HEIGHT))
    catcher.taken = taken
    catcher.on_change = save.setEnabled_
    alert.setAccessoryView_(catcher)
    alert.window().setInitialFirstResponder_(catcher)
    NSApplication.sharedApplication().activateIgnoringOtherApps_(True)
    if alert.runModal() != _FIRST_BUTTON:
        return None
    return catcher.shortcut
