"""The hook icon, drawn in code: no image files, and SF Symbols has no hook.

A template image, so macOS draws it black or white to suit the menu bar. A
known spot puts bait on the hook; a resting hook is drawn faded.
"""

from AppKit import NSBezierPath, NSColor, NSImage, NSMakeRect
from Foundation import NSMakePoint, NSMakeSize

from hookjump.face import Mood

SIZE = 18.0  # points; menu bar icons are 16-18 pt tall


def hook_icon(mood: Mood) -> NSImage:
    def draw(rect: object) -> bool:
        alpha = 0.35 if mood is Mood.RESTING else 1.0
        NSColor.blackColor().colorWithAlphaComponent_(alpha).set()

        eye = NSBezierPath.bezierPathWithOvalInRect_(NSMakeRect(7.6, 13.6, 2.8, 2.8))
        eye.setLineWidth_(1.3)
        eye.stroke()

        hook = NSBezierPath.bezierPath()
        hook.setLineWidth_(1.6)
        hook.setLineCapStyle_(1)  # round
        hook.setLineJoinStyle_(1)
        hook.moveToPoint_(NSMakePoint(9.0, 13.6))  # the shank, down from the eye
        hook.lineToPoint_(NSMakePoint(9.0, 6.5))
        # The bend: half a circle around (6, 6.5), down and up the other side.
        hook.appendBezierPathWithArcWithCenter_radius_startAngle_endAngle_clockwise_(
            NSMakePoint(6.0, 6.5), 3.0, 0.0, 180.0, True
        )
        hook.lineToPoint_(NSMakePoint(3.0, 9.6))  # the point
        hook.lineToPoint_(NSMakePoint(4.4, 8.6))  # the barb
        hook.stroke()

        if mood is Mood.KNOWN:
            bait = NSBezierPath.bezierPathWithOvalInRect_(
                NSMakeRect(4.75, 5.35, 2.5, 2.5)
            )
            bait.fill()
        return True

    image = NSImage.imageWithSize_flipped_drawingHandler_(
        NSMakeSize(SIZE, SIZE), False, draw
    )
    image.setTemplate_(True)
    return image
