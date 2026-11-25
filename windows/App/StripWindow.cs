using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// The strip by the clock that says what a key did: a few seconds of the
/// phone's sea, with the float sliding to the hook, or the hook coming down.
/// </summary>
/// <remarks>
/// A layered window: drawn with per-pixel alpha, always on top, never takes the
/// focus, and lets clicks through to whatever is under it.
/// </remarks>
internal sealed unsafe class StripWindow : IDisposable
{
    // The strip's own grid, in pixels at 100 % scale.
    private const float Width = 320, Height = 108, Radius = 12, Left = 16, Right = 304, LineY = 42, Hook = 0.6f;
    private const float WordsUnderLine = 74; // below a hook hanging from the float

    private readonly float _scale;
    private readonly int _width, _height;
    private readonly nint _window, _dc, _dib, _oldBitmap, _bits;
    private StripScene? _scene;
    private long _start;

    public StripWindow()
    {
        _scale = GetDpiForSystem() / 96f;
        _width = (int)MathF.Ceiling(Width * _scale);
        _height = (int)MathF.Ceiling(Height * _scale);
        var name = "hookjump-strip";
        fixed (char* className = name)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                Proc = &WindowProc,
                Instance = GetModuleHandle(0),
                ClassName = className,
            };
            RegisterClassEx(&windowClass);
        }
        _window = CreateWindowEx(
            WsExLayered | WsExTransparent | WsExTopmost | WsExToolWindow | WsExNoActivate, name, name, WsPopup,
            0, 0, _width, _height, 0, 0, GetModuleHandle(0), 0);
        _dc = CreateCompatibleDC(0);
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = _width,
            Height = -_height, // top-down rows
            Planes = 1,
            BitCount = 32,
        };
        _dib = CreateDIBSection(_dc, &header, 0, out _bits, 0, 0);
        _oldBitmap = SelectObject(_dc, _dib);
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint window, uint message, nint w, nint l) => DefWindowProc(window, message, w, l);

    public void Show(StripScene scene)
    {
        _scene = scene;
        _start = Stopwatch.GetTimestamp();
        Frame();
        ShowWindow(_window, SwShowNoActivate);
    }

    /// <summary>Draws the strip as it is now; false once it has gone.</summary>
    public bool Frame()
    {
        if (_scene is not { } scene) return false;
        var frame = Strip.At(scene, Stopwatch.GetElapsedTime(_start).TotalSeconds);
        if (frame.Done)
        {
            ShowWindow(_window, SwHide);
            _scene = null;
            return false;
        }
        Draw(scene, frame);
        Present(frame.Alpha);
        return true;
    }

    public void Dispose()
    {
        SelectObject(_dc, _oldBitmap);
        DeleteObject(_dib);
        DeleteDC(_dc);
        DestroyWindow(_window);
    }

    private void Present(double alpha)
    {
        Rect area;
        SystemParametersInfo(SpiGetWorkArea, 0, &area, 0);
        var margin = (int)(12 * _scale);
        var at = new Point { X = area.Right - _width - margin, Y = area.Bottom - _height - margin };
        var size = new Native.Size { Width = _width, Height = _height };
        var from = new Point();
        var blend = new BlendFunction { SourceConstantAlpha = (byte)Math.Round(alpha * 255), AlphaFormat = 1 };
        UpdateLayeredWindow(_window, 0, &at, &size, _dc, &from, 0, &blend, UlwAlpha);
    }

    private void Draw(StripScene scene, StripFrame frame)
    {
        GdipCreateBitmapFromScan0(_width, _height, _width * 4, PixelFormat32bppPArgb, _bits, out var bitmap);
        GdipGetImageGraphicsContext(bitmap, out var g);
        GdipGraphicsClear(g, 0);
        GdipSetSmoothingMode(g, SmoothingAntiAlias);
        GdipSetTextRenderingHint(g, TextAntiAlias);
        GdipScaleWorldTransform(g, _scale, _scale, 0);

        // The water, with rounded corners and a contour line round it.
        GdipCreatePath(0, out var shape);
        const float d = Radius * 2;
        GdipAddPathArc(shape, 0.5f, 0.5f, d, d, 180, 90);
        GdipAddPathArc(shape, Width - d - 0.5f, 0.5f, d, d, 270, 90);
        GdipAddPathArc(shape, Width - d - 0.5f, Height - d - 0.5f, d, d, 0, 90);
        GdipAddPathArc(shape, 0.5f, Height - d - 0.5f, d, d, 90, 90);
        GdipClosePathFigure(shape);
        using (var water = new Brush(Sea.Water)) GdipFillPath(g, water.Handle, shape);
        using (var edge = new Pen(Sea.Contour, 1)) GdipDrawPath(g, edge.Handle, shape);
        GdipDeletePath(shape);

        Text(g, scene.Title, Fonts.Barlow(17), 14, 7, Width - 28, 24, oneLine: true);
        var wordsTop = 34f;
        if (scene.HasLine)
        {
            var duration = (float)scene.DurationMs!.Value;
            float X(double ms) => Left + (Right - Left) * (float)Math.Clamp(ms / duration, 0, 1);
            var hookX = frame.HookMs is { } hookMs ? X(hookMs) : Right;

            // The line: solid to the hook, dotted after it, as on the phone.
            using (var line = new Pen(Sea.Ink, 2)) GdipDrawLine(g, line.Handle, Left, LineY, hookX, LineY);
            if (hookX < Right)
            {
                using var dots = new Pen(Sea.Ink, 2);
                var dashes = stackalloc float[] { 1f, 2.5f }; // in line widths: 2 px dots, 5 px apart
                GdipSetPenDashArray(dots.Handle, dashes, 2);
                GdipDrawLine(g, dots.Handle, hookX, LineY, Right, LineY);
            }

            var floatX = frame.FloatMs is { } floatMs ? X(floatMs) + (float)frame.Wiggle : (float?)null;
            if (frame.HookMs is not null)
            {
                // Under the float, the hook hangs from it, as on a real line;
                // apart from it, the hook hangs from the line itself.
                var under = floatX is { } fx ? Math.Clamp(1 - Math.Abs(fx - hookX) / 16, 0, 1) : 0;
                // A new pin's hook comes down from above, and shows as it comes.
                var hy = LineY + 11 * under - (float)(1 - frame.HookDepth) * 30;
                DrawHook(g, hookX, hy, 0.3 + 0.7 * frame.HookDepth);
            }
            if (floatX is { } x) DrawFloat(g, x, LineY);
            wordsTop = WordsUnderLine;
        }
        Text(g, scene.Words, Fonts.Plex(11.5f), 14, wordsTop, Width - 28, Height - wordsTop - 4, oneLine: false);

        GdipDeleteGraphics(g);
        GdipDisposeImage(bitmap);
    }

    /// <summary>The phone's hook, smaller: the shank down from the line, the bend, the barb, and the eye.</summary>
    private static void DrawHook(nint g, float hx, float hy, double alpha)
    {
        float S(float v) => v * Hook;
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

    /// <summary>The phone's float, smaller: red, with a band of foam round its middle.</summary>
    private static void DrawFloat(nint g, float fx, float fy)
    {
        using (var buoy = new Brush(Sea.Buoy)) GdipFillEllipse(g, buoy.Handle, fx - 6, fy - 9, 12, 18);
        using (var foam = new Brush(Sea.Foam)) GdipFillRectangle(g, foam.Handle, fx - 6, fy - 1.2f, 12, 2.4f);
    }

    private static void Text(nint g, string text, nint font, float x, float y, float width, float height, bool oneLine)
    {
        GdipCreateStringFormat(oneLine ? StringNoWrap : 0, 0, out var format);
        GdipSetStringFormatTrimming(format, TrimEllipsis);
        var box = new RectF(x, y, width, height);
        using (var ink = new Brush(Sea.Ink)) GdipDrawString(g, text, text.Length, font, &box, format, ink.Handle);
        GdipDeleteStringFormat(format);
        GdipDeleteFont(font);
    }

    private sealed class Brush : IDisposable
    {
        public Brush(uint argb) => GdipCreateSolidFill(argb, out Handle);

        public readonly nint Handle;

        public void Dispose() => GdipDeleteBrush(Handle);
    }

    private sealed class Pen : IDisposable
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
