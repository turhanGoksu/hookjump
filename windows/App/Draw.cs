using System;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// What the strip and the panel both draw, as the phone draws it: the hook,
/// the float, words in the phone's typefaces. Sizes are the phone's, times a
/// scale.
/// </summary>
internal static unsafe class Draw
{
    /// <summary>The phone's hook: the shank down from the eye, the bend, the barb, and the eye.</summary>
    public static void Hook(nint g, float hx, float hy, float scale, double alpha = 1)
    {
        float S(float v) => v * scale;
        var color = Sea.Faded(Sea.Buoy, alpha);
        GdipCreatePath(0, out var hook);
        GdipAddPathLine(hook, hx, hy, hx, hy + S(18));
        GdipAddPathArc(hook, hx - S(22), hy + S(7), S(22), S(22), 0, 180);
        GdipAddPathLine(hook, hx - S(22), hy + S(18), hx - S(22), hy + S(12));
        GdipAddPathLine(hook, hx - S(22), hy + S(12), hx - S(16), hy + S(18));
        using (var pen = new Pen(color, S(3.5f))) GdipDrawPath(g, pen.Handle, hook);
        GdipDeletePath(hook);
        using (var water = new Brush(Sea.Water)) GdipFillEllipse(g, water.Handle, hx - S(4.5f), hy - S(7.5f), S(9), S(9));
        using (var ring = new Pen(color, S(3))) GdipDrawEllipse(g, ring.Handle, hx - S(4.5f), hy - S(7.5f), S(9), S(9));
    }

    /// <summary>The phone's float: red, with a band of foam round its middle.</summary>
    public static void Float(nint g, float fx, float fy, float scale)
    {
        using (var buoy = new Brush(Sea.Buoy))
            GdipFillEllipse(g, buoy.Handle, fx - 10 * scale, fy - 15 * scale, 20 * scale, 30 * scale);
        using (var foam = new Brush(Sea.Foam))
            GdipFillRectangle(g, foam.Handle, fx - 10 * scale, fy - 2 * scale, 20 * scale, 4 * scale);
    }

    /// <summary>Words in a box, cut with "…" when they do not fit. Deletes the font.</summary>
    public static void Text(nint g, string text, nint font, Box box, uint color, bool oneLine, int align = StringAlignNear)
    {
        GdipCreateStringFormat(oneLine ? StringNoWrap : 0, 0, out var format);
        GdipSetStringFormatTrimming(format, TrimEllipsis);
        GdipSetStringFormatAlign(format, align);
        var layout = new RectF(box.X, box.Y, box.Width, box.Height);
        using (var ink = new Brush(color)) GdipDrawString(g, text, text.Length, font, &layout, format, ink.Handle);
        GdipDeleteStringFormat(format);
        GdipDeleteFont(font);
    }

    /// <summary>How wide words are in this font. Keeps the font.</summary>
    public static float Measure(nint g, string text, nint font)
    {
        GdipCreateStringFormat(StringNoWrap, 0, out var format);
        var layout = new RectF(0, 0, 10_000, 1_000);
        RectF bounds;
        GdipMeasureString(g, text, text.Length, font, &layout, format, &bounds, null, null);
        GdipDeleteStringFormat(format);
        return bounds.Width;
    }

    /// <summary>Turns what is drawn next by <paramref name="degrees"/> round a point, until <see cref="Restore"/>.</summary>
    public static uint Turn(nint g, float x, float y, float degrees)
    {
        GdipSaveGraphics(g, out var state);
        GdipTranslateWorldTransform(g, x, y, 0);
        GdipRotateWorldTransform(g, degrees, 0);
        GdipTranslateWorldTransform(g, -x, -y, 0);
        return state;
    }

    public static void Restore(nint g, uint state) => GdipRestoreGraphics(g, state);

    /// <summary>A rectangle with rounded corners, as a path to fill or draw; delete it with GdipDeletePath.</summary>
    public static nint Rounded(float x, float y, float width, float height, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(width, height));
        GdipCreatePath(0, out var path);
        GdipAddPathArc(path, x, y, d, d, 180, 90);
        GdipAddPathArc(path, x + width - d, y, d, d, 270, 90);
        GdipAddPathArc(path, x + width - d, y + height - d, d, d, 0, 90);
        GdipAddPathArc(path, x, y + height - d, d, d, 90, 90);
        GdipClosePathFigure(path);
        return path;
    }

    public sealed class Brush : IDisposable
    {
        public Brush(uint argb) => GdipCreateSolidFill(argb, out Handle);

        public readonly nint Handle;

        public void Dispose() => GdipDeleteBrush(Handle);
    }

    public sealed class Pen : IDisposable
    {
        public Pen(uint argb, float width)
        {
            GdipCreatePen1(argb, width, 0, out Handle);
            GdipSetPenStartCap(Handle, LineCapRound);
            GdipSetPenEndCap(Handle, LineCapRound);
            GdipSetPenLineJoin(Handle, LineJoinRound);
        }

        public readonly nint Handle;

        public void Dispose() => GdipDeletePen(Handle);
    }
}
