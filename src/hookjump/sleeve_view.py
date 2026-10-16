"""Draws the record sleeve at the top of the menu, and turns a click into a pin.

Moving the mouse over the line shows which second a click would pin. Colors
are the system's own, so the strip follows light and dark mode.
"""

import objc
from AppKit import (
    NSBezierPath,
    NSColor,
    NSFont,
    NSFontAttributeName,
    NSForegroundColorAttributeName,
    NSMakeRect,
    NSMutableParagraphStyle,
    NSParagraphStyleAttributeName,
    NSTrackingActiveAlways,
    NSTrackingArea,
    NSTrackingInVisibleRect,
    NSTrackingMouseEnteredAndExited,
    NSTrackingMouseMoved,
    NSView,
)
from Foundation import NSMakePoint, NSString

from hookjump.sleeve import Sleeve, fraction, ms_at
from hookjump.words import clock

WIDTH, HEIGHT = 320.0, 84.0
_PAD = 14.0
_LINE_Y = 44.0
_TRUNCATE_TAIL = 4  # NSLineBreakByTruncatingTail


class SleeveView(NSView):
    def initWithFrame_(self, frame):  # noqa: N802
        self = objc.super(SleeveView, self).initWithFrame_(frame)
        if self is None:
            return None
        self.sleeve = None
        self.hover_x = None
        self.on_pick = None  # called with the moment clicked, in ms
        options = (
            NSTrackingMouseMoved
            | NSTrackingMouseEnteredAndExited
            | NSTrackingActiveAlways
            | NSTrackingInVisibleRect
        )
        self.addTrackingArea_(
            NSTrackingArea.alloc().initWithRect_options_owner_userInfo_(
                self.bounds(), options, self, None
            )
        )
        return self

    def isFlipped(self):  # noqa: N802  (y grows downwards, like text)
        return True

    @objc.python_method
    def show(self, sleeve: Sleeve | None) -> None:
        self.sleeve = sleeve
        self.setNeedsDisplay_(True)

    # Mouse ---------------------------------------------------------------------

    def mouseMoved_(self, event):  # noqa: N802
        self.hover_x = self.convertPoint_fromView_(event.locationInWindow(), None).x
        self.setNeedsDisplay_(True)

    def mouseExited_(self, _event):  # noqa: N802
        self.hover_x = None
        self.setNeedsDisplay_(True)

    def mouseUp_(self, event):  # noqa: N802
        if self.sleeve is None or self.on_pick is None:
            return
        x = self.convertPoint_fromView_(event.locationInWindow(), None).x
        self.on_pick(self._moment_at(x))
        self.enclosingMenuItem().menu().cancelTracking()

    # Drawing -------------------------------------------------------------------

    def drawRect_(self, _rect):  # noqa: N802
        sleeve = self.sleeve
        if sleeve is None:
            self._text("Spotify is not playing anything", _PAD, 12, 13, "secondary")
            return
        self._title(sleeve.title)
        right = WIDTH - _PAD
        NSColor.tertiaryLabelColor().set()
        NSBezierPath.fillRect_(NSMakeRect(_PAD, _LINE_Y - 1, right - _PAD, 2))

        NSColor.labelColor().set()
        for ms in sleeve.rewinds:  # rewinds: ticks above the line
            NSBezierPath.fillRect_(NSMakeRect(self._x(ms) - 0.75, _LINE_Y - 9, 1.5, 7))
        NSColor.secondaryLabelColor().set()
        for ms in sleeve.skips:  # forward skips: shorter ticks below
            NSBezierPath.fillRect_(NSMakeRect(self._x(ms) - 0.75, _LINE_Y + 2, 1.5, 5))

        if sleeve.hook_ms is not None:
            self._diamond(self._x(sleeve.hook_ms))
        if sleeve.now_ms is not None:
            NSColor.controlAccentColor().set()
            x = self._x(sleeve.now_ms)
            NSBezierPath.bezierPathWithOvalInRect_(
                NSMakeRect(x - 4, _LINE_Y - 4, 8, 8)
            ).fill()

        self._text("0:00", _PAD, 58, 11, "secondary")
        self._text(clock(sleeve.duration_ms), right, 58, 11, "secondary", "right")
        caption = sleeve.caption()
        if self.hover_x is not None:
            moment = self._moment_at(self.hover_x)
            caption = f"click to pin {clock(moment)}"
            NSColor.secondaryLabelColor().set()
            NSBezierPath.fillRect_(NSMakeRect(self._x(moment) - 0.5, 32, 1, 24))
        self._text(caption, WIDTH / 2, 58, 11, "secondary", "center")

    @objc.python_method
    def _x(self, ms: int) -> float:
        return _PAD + fraction(ms, self.sleeve.duration_ms) * (WIDTH - 2 * _PAD)

    @objc.python_method
    def _moment_at(self, x: float) -> int:
        return ms_at((x - _PAD) / (WIDTH - 2 * _PAD), self.sleeve.duration_ms)

    @objc.python_method
    def _diamond(self, x: float) -> None:
        path = NSBezierPath.bezierPath()
        path.moveToPoint_(NSMakePoint(x, _LINE_Y - 5.5))
        path.lineToPoint_(NSMakePoint(x + 5.5, _LINE_Y))
        path.lineToPoint_(NSMakePoint(x, _LINE_Y + 5.5))
        path.lineToPoint_(NSMakePoint(x - 5.5, _LINE_Y))
        path.closePath()
        NSColor.labelColor().set()
        path.fill()

    @objc.python_method
    def _title(self, title: str) -> None:
        style = NSMutableParagraphStyle.alloc().init()
        style.setLineBreakMode_(_TRUNCATE_TAIL)
        attributes = {
            NSFontAttributeName: NSFont.boldSystemFontOfSize_(13),
            NSForegroundColorAttributeName: NSColor.labelColor(),
            NSParagraphStyleAttributeName: style,
        }
        NSString.stringWithString_(title).drawInRect_withAttributes_(
            NSMakeRect(_PAD, 9, WIDTH - 2 * _PAD, 18), attributes
        )

    @objc.python_method
    def _text(
        self, text: str, x: float, y: float, size: float, tone: str, align: str = "left"
    ) -> None:
        color = (
            NSColor.secondaryLabelColor()
            if tone == "secondary"
            else NSColor.labelColor()
        )
        attributes = {
            NSFontAttributeName: NSFont.systemFontOfSize_(size),
            NSForegroundColorAttributeName: color,
        }
        string = NSString.stringWithString_(text)
        width = string.sizeWithAttributes_(attributes).width
        if align == "center":
            x -= width / 2
        elif align == "right":
            x -= width
        string.drawAtPoint_withAttributes_(NSMakePoint(x, y), attributes)
