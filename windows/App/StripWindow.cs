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

    private readonly nint _window;
    private float _scale = 1;
    private int _width, _height;
    private nint _dc, _dib, _oldBitmap, _bits;
    private Point _at; // where it shows, on the screen the listener works on
    private StripScene? _scene;
    private long _start;

    public StripWindow()
    {
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
            0, 0, 1, 1, 0, 0, GetModuleHandle(0), 0);
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint window, uint message, nint w, nint l) => DefWindowProc(window, message, w, l);

    /// <summary>
    /// Shows the strip by the clock of the screen the listener works on (the
    /// one with the window in front), at that screen's scale.
    /// </summary>
    public void Show(StripScene scene)
    {
        var (work, dpi) = Screen.Of(Screen.Working());
        _scale = dpi / 96f;
        var (width, height) = (Placement.Pixels(Width, dpi), Placement.Pixels(Height, dpi));
        if (width != _width || height != _height)
        {
            DropCanvas();
            (_width, _height) = (width, height);
            MakeCanvas();
        }
        var (x, y) = Placement.Corner(work.Right, work.Bottom, _width, _height, dpi);
        _at = new Point { X = x, Y = y };
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
        Render(scene, frame);
        Present(frame.Alpha);
        return true;
    }

    public void Dispose()
    {
        DropCanvas();
        DestroyWindow(_window);
    }

    private void MakeCanvas()
    {
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

    private void DropCanvas()
    {
        if (_dc == 0) return;
        SelectObject(_dc, _oldBitmap);
        DeleteObject(_dib);
        DeleteDC(_dc);
        _dc = 0;
    }

    private void Present(double alpha)
    {
        var at = _at;
        var size = new Native.Size { Width = _width, Height = _height };
        var from = new Point();
        var blend = new BlendFunction { SourceConstantAlpha = (byte)Math.Round(alpha * 255), AlphaFormat = 1 };
        UpdateLayeredWindow(_window, 0, &at, &size, _dc, &from, 0, &blend, UlwAlpha);
    }

    private void Render(StripScene scene, StripFrame frame)
    {
        GdipCreateBitmapFromScan0(_width, _height, _width * 4, PixelFormat32bppPArgb, _bits, out var bitmap);
        GdipGetImageGraphicsContext(bitmap, out var g);
        GdipGraphicsClear(g, 0);
        GdipSetSmoothingMode(g, SmoothingAntiAlias);
        GdipSetTextRenderingHint(g, TextAntiAlias);
        GdipScaleWorldTransform(g, _scale, _scale, 0);

        // The water, with rounded corners and a contour line round it.
        var shape = Draw.Rounded(0.5f, 0.5f, Width - 1, Height - 1, Radius);
        using (var water = new Draw.Brush(Sea.Water)) GdipFillPath(g, water.Handle, shape);
        using (var edge = new Draw.Pen(Sea.Contour, 1)) GdipDrawPath(g, edge.Handle, shape);
        GdipDeletePath(shape);

        Draw.Text(g, scene.Title, Fonts.Barlow(17), new Box(14, 7, Width - 28, 24), Sea.Ink, oneLine: true);
        var wordsTop = 34f;
        if (scene.HasLine)
        {
            var duration = (float)scene.DurationMs!.Value;
            float X(double ms) => Left + (Right - Left) * (float)Math.Clamp(ms / duration, 0, 1);
            var hookX = frame.HookMs is { } hookMs ? X(hookMs) : Right;

            // The line: solid to the hook, dotted after it, as on the phone.
            using (var line = new Draw.Pen(Sea.Ink, 2)) GdipDrawLine(g, line.Handle, Left, LineY, hookX, LineY);
            if (hookX < Right)
            {
                using var dots = new Draw.Pen(Sea.Ink, 2);
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
                Draw.Hook(g, hookX, hy, Hook, 0.3 + 0.7 * frame.HookDepth);
            }
            if (floatX is { } x) Draw.Float(g, x, LineY, Hook);
            wordsTop = WordsUnderLine;
        }
        Draw.Text(g, scene.Words, Fonts.Plex(11.5f), new Box(14, wordsTop, Width - 28, Height - wordsTop - 4), Sea.Ink, oneLine: false);

        GdipDeleteGraphics(g);
        GdipDisposeImage(bitmap);
    }
}
