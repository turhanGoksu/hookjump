using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// The hook for the notification area, drawn in code like the Mac's
/// (src/hookjump/icon.py): bait when the spot is known, empty when not, faded
/// while resting. White on a dark taskbar, black on a light one.
/// </summary>
internal static class HookIcon
{
    private const float Grid = 18; // the Mac's 18 x 18 grid, here with y going down

    /// <summary>A new icon handle; the caller destroys it with DestroyIcon.</summary>
    public static nint Draw(Mood mood, bool lightTaskbar, int pixels)
    {
        GdipCreateBitmapFromScan0(pixels, pixels, 0, PixelFormat32bppArgb, 0, out var bitmap);
        GdipGetImageGraphicsContext(bitmap, out var g);
        GdipSetSmoothingMode(g, SmoothingAntiAlias);
        GdipScaleWorldTransform(g, pixels / Grid, pixels / Grid, 0);

        var alpha = mood == Mood.Resting ? 0x59u : 0xFFu; // 35 % while resting
        var color = (alpha << 24) | (lightTaskbar ? 0x000000u : 0xFFFFFFu);
        GdipCreatePen1(color, 1.3f, 0, out var eyePen);
        GdipDrawEllipse(g, eyePen, 7.6f, 1.6f, 2.8f, 2.8f); // the eye
        GdipDeletePen(eyePen);

        GdipCreatePen1(color, 1.6f, 0, out var pen);
        GdipSetPenStartCap(pen, LineCapRound);
        GdipSetPenEndCap(pen, LineCapRound);
        GdipSetPenLineJoin(pen, LineJoinRound);
        GdipCreatePath(0, out var hook);
        GdipAddPathLine(hook, 9f, 4.4f, 9f, 11.5f); // the shank, down from the eye
        GdipAddPathArc(hook, 3f, 8.5f, 6f, 6f, 0f, 180f); // the bend, around (6, 11.5)
        GdipAddPathLine(hook, 3f, 11.5f, 3f, 8.4f); // the point
        GdipAddPathLine(hook, 3f, 8.4f, 4.4f, 9.4f); // the barb
        GdipDrawPath(g, pen, hook);
        GdipDeletePath(hook);
        GdipDeletePen(pen);

        if (mood == Mood.Known)
        {
            GdipCreateSolidFill(color, out var bait);
            GdipFillEllipse(g, bait, 4.75f, 10.15f, 2.5f, 2.5f);
            GdipDeleteBrush(bait);
        }

        GdipDeleteGraphics(g);
        GdipCreateHICONFromBitmap(bitmap, out var icon);
        GdipDisposeImage(bitmap);
        return icon;
    }
}
