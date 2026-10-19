"""The hook icon, drawn in code: no image files, and SF Symbols has no hook.

In the menu bar it is a template image, so macOS draws it black or white to
suit the bar; a known spot puts bait on the hook, and a resting hook is faded.
The same hook, larger and on a dark tile, is the icon of hookjump.app.
"""

from AppKit import (
    NSAffineTransform,
    NSBezierPath,
    NSBitmapImageRep,
    NSCalibratedRGBColorSpace,
    NSColor,
    NSGraphicsContext,
    NSImage,
    NSMakeRect,
)
from Foundation import NSMakePoint, NSMakeSize

from hookjump.face import Mood

SIZE = 18.0  # points; menu bar icons are 16-18 pt tall
_PNG = 4  # NSBitmapImageFileTypePNG


def _draw_hook(bait: bool) -> None:
    """The hook in the current color, on an 18 x 18 grid."""
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

    if bait:
        NSBezierPath.bezierPathWithOvalInRect_(NSMakeRect(4.75, 5.35, 2.5, 2.5)).fill()


def hook_icon(mood: Mood) -> NSImage:
    def draw(rect: object) -> bool:
        alpha = 0.35 if mood is Mood.RESTING else 1.0
        NSColor.blackColor().colorWithAlphaComponent_(alpha).set()
        _draw_hook(bait=mood is Mood.KNOWN)
        return True

    image = NSImage.imageWithSize_flipped_drawingHandler_(
        NSMakeSize(SIZE, SIZE), False, draw
    )
    image.setTemplate_(True)
    return image


def app_icon_png(pixels: int) -> bytes:
    """The app icon as a PNG, ``pixels`` wide: a white hook on a dark tile."""
    rep = NSBitmapImageRep.alloc().initWithBitmapDataPlanes_pixelsWide_pixelsHigh_bitsPerSample_samplesPerPixel_hasAlpha_isPlanar_colorSpaceName_bytesPerRow_bitsPerPixel_(  # noqa: E501
        None, pixels, pixels, 8, 4, True, False, NSCalibratedRGBColorSpace, 0, 0
    )
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.setCurrentContext_(
        NSGraphicsContext.graphicsContextWithBitmapImageRep_(rep)
    )
    # macOS app icons sit on a rounded square with a margin around it.
    margin, side = pixels * 0.1, pixels * 0.8
    NSColor.colorWithCalibratedRed_green_blue_alpha_(0.12, 0.14, 0.19, 1).set()
    NSBezierPath.bezierPathWithRoundedRect_xRadius_yRadius_(
        NSMakeRect(margin, margin, side, side), side * 0.22, side * 0.22
    ).fill()
    scale = side * 0.75 / SIZE
    move = NSAffineTransform.transform()
    move.translateXBy_yBy_(pixels / 2, pixels / 2)
    move.scaleBy_(scale)
    # The hook's own middle on its grid (strokes included): x 2.2-11.05, y 2.7-17.05.
    move.translateXBy_yBy_(-6.6, -9.9)
    move.concat()
    NSColor.whiteColor().set()
    _draw_hook(bait=True)
    NSGraphicsContext.restoreGraphicsState()
    return bytes(rep.representationUsingType_properties_(_PNG, {}))
