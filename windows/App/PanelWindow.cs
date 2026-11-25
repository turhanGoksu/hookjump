using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// The sea panel, opened by a click on the hook: the playing song as a line
/// through the phone's water, what the hook is, and Jump and Pin. A click on
/// the line pins that second.
/// </summary>
/// <remarks>
/// A window above the clock, as Windows' own panels there: it closes when
/// clicked away from or with Esc, and Tab, Enter and Space work its buttons.
/// What goes where is decided in <see cref="Panel"/>; this draws it and
/// passes on what is pressed.
/// </remarks>
internal sealed unsafe class PanelWindow : IDisposable
{
    private const nint FrameTimer = 1;
    private const uint FrameMs = 16; // while something moves
    private const uint StillMs = 100; // with motion off, the float still moves on between Spotify's reports

    private static PanelWindow? _open; // the window procedure finds the panel here

    private readonly nint _window;
    private readonly Action<PanelPart, long?> _press;
    private float _scale = 1;
    private int _width, _height;
    private nint _dc, _dib, _oldBitmap, _bits;
    private Chart? _chart;
    private ShortcutChoice? _keys;
    private string? _answer;
    private PanelPart _hover, _down, _focus;
    private bool _focusShown, _tracking;
    private float? _pointerX;
    private long _closedAt;

    // Motion, slow and calm as on the phone, and none at all when Windows'
    // animation effects are off.
    private bool _motion;
    private readonly SeaClock _sea = new();
    private long _lastFrame;
    private uint _interval; // the frame timer's; 0 while it is stopped
    private HookWords? _seen;
    private HookChange? _change;
    private long _changeAt;
    private float? _targetX, _drawnX; // where the float should be, and where it was drawn
    private double _glideFrom;
    private long _glideAt;
    private bool _gliding;

    /// <param name="press">What to do when a part is pressed; the line passes the moment pressed.</param>
    public PanelWindow(Action<PanelPart, long?> press)
    {
        _press = press;
        _open = this;
        var name = "hookjump-panel";
        fixed (char* className = name)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                Style = CsDropShadow,
                Proc = &WindowProc,
                Instance = GetModuleHandle(0),
                Cursor = LoadCursor(0, IdcArrow),
                ClassName = className,
            };
            RegisterClassEx(&windowClass);
        }
        _window = CreateWindowEx(WsExToolWindow | WsExTopmost, name, "hookjump", WsPopup, 0, 0, 1, 1, 0, 0, GetModuleHandle(0), 0);
        // Windows 11's rounded corners, with the sea's contour as the edge.
        var round = DwmRound;
        DwmSetWindowAttribute(_window, DwmWindowCornerPreference, &round, sizeof(uint));
        var edge = Colorref(Sea.Contour);
        DwmSetWindowAttribute(_window, DwmBorderColor, &edge, sizeof(uint));
    }

    public bool Visible => IsWindowVisible(_window);

    /// <summary>A click on the hook: opens the panel, or closes it if open.</summary>
    public void Toggle(Chart? chart, ShortcutChoice? keys)
    {
        if (Visible) Hide();
        else if (Panel.OpensOnClick(Now(), _closedAt)) Show(chart, keys);
    }

    public void Show(Chart? chart, ShortcutChoice? keys)
    {
        _keys = keys;
        _answer = null;
        _focus = PanelPart.None;
        _focusShown = false;
        _motion = MotionWanted();
        // What is there when it opens is simply there: only changes move.
        _seen = chart is null ? null : HookWords.Of(chart);
        _change = null;
        (_targetX, _drawnX, _gliding) = (null, null, false);
        _lastFrame = Stopwatch.GetTimestamp();
        Place(chart);
        SetForegroundWindow(_window);
        Schedule();
    }

    /// <summary>
    /// The song as it is now; a song starting or ending changes the panel's
    /// height, and a new or removed pin is cast or reeled.
    /// </summary>
    public void Update(Chart? chart)
    {
        if (!Visible) return;
        if ((chart is null) != (_chart is null)) Place(chart);
        _chart = chart;
        var after = chart is null ? null : HookWords.Of(chart);
        if (after is not null && HookChange.Of(_seen, after, _motion) is { } change)
            (_change, _changeAt) = (change, Stopwatch.GetTimestamp());
        _seen = after;
        Schedule();
        Invalidate();
    }

    /// <summary>What a press or a key did, said in the panel while it is open.</summary>
    public void Say(string words)
    {
        _answer = words;
        Invalidate();
    }

    public void Hide()
    {
        if (!Visible) return;
        KillTimer(_window, FrameTimer);
        _interval = 0;
        ShowWindow(_window, SwHide);
        _closedAt = Now();
        (_hover, _down, _pointerX) = (PanelPart.None, PanelPart.None, null);
    }

    public void Dispose()
    {
        DropCanvas();
        DestroyWindow(_window);
        _open = null;
    }

    private static long Now() => Environment.TickCount64;

    private static double Since(long timestamp) => Stopwatch.GetElapsedTime(timestamp).TotalSeconds;

    private static bool MotionWanted()
    {
        var on = 1;
        SystemParametersInfo(SpiGetClientAreaAnimation, 0, &on, 0);
        return on != 0;
    }

    // When it draws: every frame while something moves, now and then while
    // only the float goes on with the song, and not at all when nothing moves.

    private void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        _sea.Advance((long)Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds, _chart?.Playing ?? false, _motion);
        _lastFrame = now;
        if (_chart is { Playing: true } || Moving()) Invalidate();
        Schedule();
    }

    /// <summary>A cast or a reel, the ripple after a cast, a gliding float, or the sea of a playing song.</summary>
    private bool Moving()
    {
        if (_change is { } change && Since(_changeAt) < change.Seconds + (change.Cast ? HookChange.RippleSeconds : 0))
            return true;
        return _gliding || (_motion && _chart is { Playing: true });
    }

    private void Schedule()
    {
        var wanted = !Visible ? 0 : Moving() ? FrameMs : _chart is { Playing: true } ? StillMs : 0;
        if (wanted == _interval) return;
        if (wanted == 0) KillTimer(_window, FrameTimer);
        else SetTimer(_window, FrameTimer, wanted, 0);
        _interval = wanted;
    }

    private static uint Colorref(uint argb) => ((argb & 0xFF) << 16) | (argb & 0xFF00) | ((argb >> 16) & 0xFF);

    // Where it is: above the clock, at the scale of the screen ---------------

    private void Place(Chart? chart)
    {
        _chart = chart;
        _scale = GetDpiForWindow(_window) / 96f;
        var width = (int)MathF.Ceiling(Panel.Width * _scale);
        var height = (int)MathF.Ceiling(Panel.HeightFor(chart is not null) * _scale);
        if (width != _width || height != _height)
        {
            DropCanvas();
            (_width, _height) = (width, height);
            MakeCanvas();
        }
        Rect area;
        SystemParametersInfo(SpiGetWorkArea, 0, &area, 0);
        var margin = (int)(12 * _scale);
        SetWindowPos(_window, -1, area.Right - _width - margin, area.Bottom - _height - margin, _width, _height, SwpShowWindow);
        Invalidate();
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

    private void Invalidate() => InvalidateRect(_window, null, false);

    // What it hears --------------------------------------------------------------

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint window, uint message, nint w, nint l)
    {
        try
        {
            if (_open is { } panel && panel._window == window && panel.Handle(message, w, l) is { } done) return done;
        }
        catch (Exception e)
        {
            // Nothing may escape into Windows; say it in the panel instead.
            _open?.Say($"Something went wrong: {e.Message}");
        }
        return DefWindowProc(window, message, w, l);
    }

    private nint? Handle(uint message, nint w, nint l)
    {
        switch (message)
        {
            case WmActivate when (w & 0xFFFF) == 0: // clicked away from
                Hide();
                return 0;
            case WmPaint:
                Paint();
                return 0;
            case WmEraseBkgnd:
                return 1; // all of it is painted, so nothing flickers
            case WmTimer when w == FrameTimer:
                Tick();
                return 0;
            case WmMouseMove:
                Pointer(l);
                return 0;
            case WmMouseLeave:
                _tracking = false;
                (_hover, _pointerX) = (PanelPart.None, null);
                Invalidate();
                return 0;
            case WmLButtonDown:
                _down = Hit(l, out _);
                Invalidate();
                return 0;
            case WmLButtonUp:
                var up = Hit(l, out var x);
                if (up != PanelPart.None && up == _down) Press(up, x);
                _down = PanelPart.None;
                Invalidate();
                return 0;
            case WmSetCursor when (l & 0xFFFF) == HtClient:
                SetCursor(LoadCursor(0, _hover == PanelPart.None ? IdcArrow : IdcHand));
                return 1;
            case WmKeyDown:
                return Key((int)w) ? 0 : (nint?)null;
        }
        return null;
    }

    private PanelPart Hit(nint l, out float x)
    {
        x = (short)(l & 0xFFFF) / _scale;
        var y = (short)((l >> 16) & 0xFFFF) / _scale;
        return Panel.Hit(x, y, _chart is not null, _chart?.Pinned ?? false);
    }

    private void Pointer(nint l)
    {
        if (!_tracking)
        {
            var track = new TrackMouse { Size = (uint)sizeof(TrackMouse), Flags = TmeLeave, Window = _window };
            _tracking = TrackMouseEvent(&track);
        }
        var hover = Hit(l, out var x);
        float? pointer = hover == PanelPart.Line ? Math.Clamp(x, Panel.LineLeft, Panel.LineRight) : null;
        if (hover == _hover && pointer == _pointerX) return;
        (_hover, _pointerX) = (hover, pointer);
        Invalidate();
    }

    private bool Key(int key)
    {
        switch (key)
        {
            case VkEscape:
                Hide();
                return true;
            case VkTab:
                var backwards = GetKeyState(VkShift) < 0;
                _focus = Panel.NextFocus(_focus, backwards, _chart is not null, _chart?.Pinned ?? false);
                _focusShown = true;
                Invalidate();
                return true;
            case VkReturn or VkSpace when _focus != PanelPart.None:
                Press(_focus, null);
                return true;
        }
        return false;
    }

    private void Press(PanelPart part, float? x)
    {
        if (part == PanelPart.Line)
        {
            if (_chart is { } chart && x is { } at) _press(part, Panel.MsAt(at, chart.DurationMs));
            return;
        }
        _press(part, null);
    }

    // What it shows --------------------------------------------------------------

    private void Paint()
    {
        PaintStruct paint;
        var dc = BeginPaint(_window, &paint);
        Render();
        BitBlt(dc, 0, 0, _width, _height, _dc, 0, 0, SrcCopy);
        EndPaint(_window, &paint);
    }

    private void Render()
    {
        GdipCreateBitmapFromScan0(_width, _height, _width * 4, PixelFormat32bppPArgb, _bits, out var bitmap);
        GdipGetImageGraphicsContext(bitmap, out var g);
        GdipGraphicsClear(g, Sea.Water);
        GdipSetSmoothingMode(g, SmoothingAntiAlias);
        GdipSetTextRenderingHint(g, TextAntiAlias);
        GdipScaleWorldTransform(g, _scale, _scale, 0);

        if (_chart is { } chart) Song(g, chart);
        else Nothing(g);

        GdipDeleteGraphics(g);
        GdipDisposeImage(bitmap);
    }

    private static void Nothing(nint g)
    {
        Draw.Text(g, "Nothing playing", Fonts.BarlowBold(26), Panel.Title, Sea.Ink, oneLine: true);
        Draw.Text(
            g, "Play a song in Spotify, and it shows here as a line.", Fonts.BarlowMedium(15),
            new Box(Panel.Pad, 48, Panel.Width - 2 * Panel.Pad, 44), Sea.Ink, oneLine: false);
    }

    private void Song(nint g, Chart chart)
    {
        Header(g, chart);
        Water(g);
        var now = chart.NowAt(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        // A cast or a reel on its way, if one is.
        var frame = _change is { } change && Since(_changeAt) < change.Seconds ? change.At(Since(_changeAt)) : null;
        Line(g, chart, now, frame);
        Times(g, chart, now);
        // The old words fade out as a cast or a reel starts, the new ones come once it is done.
        if (frame is not null && _change is { } going) HookText(g, going.Before, frame.OldWords, settled: false);
        HookText(g, HookWords.Of(chart), frame?.Words ?? 1, settled: frame is null);
        if (_answer is { } answer) Draw.Text(g, answer, Fonts.Plex(11), Panel.Answer, Sea.Ink, oneLine: false);
        Button(g, Panel.Jump, "Jump", _keys?.Jump?.ToString(), PanelPart.Jump, filled: true);
        Button(g, Panel.Pin, "Pin", _keys?.Pin?.ToString(), PanelPart.Pin, filled: false);
    }

    private static void Header(nint g, Chart chart)
    {
        Draw.Text(g, chart.Title, Fonts.BarlowBold(26), Panel.Title, Sea.Ink, oneLine: true);
        // A setting left on should not be forgotten: say so by the artist, as the phone does.
        var right = Panel.Width - Panel.Pad;
        if (chart.HookMs is not null)
        {
            if (chart.Ignored) right = Pill(g, "Not tracking this song", right);
            if (chart.LearningPaused) right = Pill(g, "Learning paused", right);
        }
        if (chart.Artist is { } artist)
            Draw.Text(g, artist, Fonts.BarlowMedium(15), Panel.Artist with { Width = right - Panel.Pad - 8 }, Sea.Ink, oneLine: true);
    }

    /// <summary>A word in an outline, ending at <paramref name="right"/>; returns where the next one ends.</summary>
    private static float Pill(nint g, string text, float right)
    {
        var font = Fonts.BarlowMedium(12);
        var width = Draw.Measure(g, text, font) + 14;
        var x = right - width;
        var outline = Draw.Rounded(x, Panel.Artist.Y + 1, width, 19, 9.5f);
        using (var pen = new Draw.Pen(Sea.Ink, 1.5f)) GdipDrawPath(g, pen.Handle, outline);
        GdipDeletePath(outline);
        Draw.Text(g, text, font, new Box(x, Panel.Artist.Y + 3, width, 18), Sea.Ink, oneLine: true, StringAlignCenter);
        return x - 6;
    }

    /// <summary>
    /// The surface and the depth contours: the sea, not data. A current moves
    /// the contours sideways, each swells and settles in its own time, and
    /// the surface's waves roll by.
    /// </summary>
    private void Water(nint g)
    {
        const float top = Panel.SeaTop, width = Panel.Width;
        var (breath, drift, swell) = (_sea.Cycle(9_000), _sea.Cycle(16_000), _sea.Cycle(7_000));
        using var contour = new Draw.Pen(Sea.Contour, 1.2f);
        var points = stackalloc PointF[7];
        for (var i = 0; i < 3; i++)
        {
            var cy = top + (i + 0.6f) * (Panel.SeaBottom - top) / 3;
            var swellOf = _motion ? 0.75 + 0.35 * Math.Sin(2 * Math.PI * (breath + i * 0.17)) : 1;
            var a = (float)(12 * swellOf) * (i % 2 == 0 ? 1 : -1);
            var dx = _motion ? (float)(Math.Sin(2 * Math.PI * (drift + i / 3.0)) * 22) : 0;
            points[0] = new(-30 + dx, cy);
            points[1] = new(width * 0.2f + dx, cy - a);
            points[2] = new(width * 0.4f + dx, cy + a);
            points[3] = new(width * 0.62f + dx, cy);
            points[4] = new(width * 0.8f + dx, cy - a * 0.8f);
            points[5] = new(width * 0.92f + dx, cy + a * 0.4f);
            points[6] = new(width + 30 + dx, cy - a * 0.2f);
            GdipCreatePath(0, out var path);
            GdipAddPathBeziers(path, points, 7);
            GdipDrawPath(g, contour.Handle, path);
            GdipDeletePath(path);
        }
        // The surface: its waves, half a step up, half a step down, rolling by.
        GdipCreatePath(0, out var surface);
        const float step = 32, rise = 5;
        var up = true;
        for (var sx = -2 * step + (_motion ? (float)swell * 2 * step : 0); sx < width + step; sx += step)
        {
            var peak = top + (up ? -rise : rise);
            // A quadratic wave, as a cubic: its handles two thirds of the way to the peak.
            GdipAddPathBezier(
                surface, sx, top, sx + step / 3, top + (peak - top) * 2 / 3, sx + step * 2 / 3, top + (peak - top) * 2 / 3,
                sx + step, top);
            up = !up;
        }
        using (var ink = new Draw.Pen(Sea.Ink, 2)) GdipDrawPath(g, ink.Handle, surface);
        GdipDeletePath(surface);
    }

    private void Line(nint g, Chart chart, long? now, HookFrame? frame)
    {
        const float top = Panel.SeaTop, left = Panel.LineLeft, right = Panel.LineRight;
        float X(long ms) => Panel.X(ms, chart.DurationMs);
        // The current bends the line a little; everything on it goes with it.
        float Y(float x) => Panel.LineY + (_motion ? Sway.Line(_sea, x) : 0);
        void Along(nint path, float from, float to)
        {
            for (var x = from; x < to;)
            {
                var next = Math.Min(to, x + 8);
                GdipAddPathLine(path, x, Y(x), next, Y(next));
                x = next;
            }
        }

        // The hook: a reel takes the old pin up and away; anything else is the chart's own.
        var reeling = frame is not null && _change is { Cast: false };
        var hookMs = reeling ? _change!.Before.Ms : chart.HookMs;
        var hookX = hookMs is { } spot ? X(spot) : (float?)null;
        double? depth = frame is null ? (hookX is null ? null : 1) : frame.Depth; // null: in the air
        var split = depth is not null ? hookX : null;

        // Minutes, as soundings at the foot of the water.
        using (var sounding = new Draw.Pen(Sea.Ink, 1.5f))
        {
            for (var minute = 60_000L; minute < chart.DurationMs; minute += 60_000)
                GdipDrawLine(g, sounding.Handle, X(minute), Panel.SeaBottom - 8, X(minute), Panel.SeaBottom - 2);
        }

        // The line comes down from the surface at 0:00 and runs along the
        // song: solid to the hook, dotted after it, as on the phone.
        using (var ink = new Draw.Pen(Sea.Ink, 2))
        {
            GdipCreatePath(0, out var path);
            GdipAddPathLine(path, left, top, left, Panel.LineY - 8);
            GdipAddPathArc(path, left, Panel.LineY - 16, 16, 16, 180, -90);
            Along(path, left + 8, split ?? right);
            GdipDrawPath(g, ink.Handle, path);
            GdipDeletePath(path);
        }
        if (split is { } from && from < right)
        {
            using var dots = new Draw.Pen(Sea.Ink, 2);
            var dashes = stackalloc float[] { 1.5f, 3.5f }; // in line widths: 3 px dots, 7 px apart
            GdipSetPenDashArray(dots.Handle, dashes, 2);
            GdipCreatePath(0, out var path);
            Along(path, from, right);
            GdipDrawPath(g, dots.Handle, path);
            GdipDeletePath(path);
        }

        // A cast or a reel: the hook in the air, and the splash at the surface.
        if (frame?.Flight is { } flight && hookX is { } airX)
        {
            var (ax, ay, turn) = Flight(airX, flight);
            var turned = Draw.Turn(g, ax, ay + 10, turn);
            Draw.Hook(g, ax, ay, 1);
            Draw.Restore(g, turned);
        }
        if (frame?.Splash is { } q && hookX is { } splashX)
        {
            using var ring = new Draw.Pen(Sea.Faded(Sea.Ink, 0.8 * (1 - q)), 1.5f);
            var r = 4 + 16 * (float)q;
            GdipDrawEllipse(g, ring.Handle, splashX - r, top - r, 2 * r, 2 * r);
        }

        // Where the listener rewound to (above) and skipped to (below).
        using (var rewound = new Draw.Pen(Sea.Ink, 3))
        {
            foreach (var ms in chart.Rewinds) GdipDrawLine(g, rewound.Handle, X(ms), Y(X(ms)) - 10, X(ms), Y(X(ms)) - 30);
        }
        using (var skipped = new Draw.Pen(Sea.Kelp, 3))
        {
            foreach (var ms in chart.Skips) GdipDrawLine(g, skipped.Handle, X(ms), Y(X(ms)) + 10, X(ms), Y(X(ms)) + 24);
        }

        // Where a click would pin.
        if (_pointerX is { } pointer)
        {
            using var mark = new Draw.Pen(Sea.Faded(Sea.Buoy, 0.6), 1.5f);
            GdipDrawLine(g, mark.Handle, pointer, top + 8, pointer, Panel.SeaBottom - 10);
        }

        var floatX = now is { } at ? FloatX(X(at)) : (float?)null;
        if (depth is { } d && hookX is { } hx)
        {
            // The hook hangs from the line; under the float, it hangs from the float.
            var under = floatX is { } fx ? Math.Clamp(1 - Math.Abs(fx - hx) / 20, 0, 1) : 0;
            var hang = Y(hx) + 3 + 15 * under;
            var hy = top + (hang - top) * (float)d;
            if (frame is null) Bubbles(g, hx, hy, chart.Playing);
            Draw.Hook(g, hx, hy, 1);
            // The pin's ripple, once the hook has settled.
            if (frame is null && _change is { Cast: true } cast && Since(_changeAt) - cast.Seconds is var after
                && after < HookChange.RippleSeconds)
            {
                var r = (float)(after / HookChange.RippleSeconds);
                using var ring = new Draw.Pen(Sea.Faded(Sea.Buoy, 1 - r), 2);
                var radius = 8 + 44 * r;
                GdipDrawEllipse(g, ring.Handle, hx - radius, hy - radius, 2 * radius, 2 * radius);
            }
        }

        // The float, where playback is: it bobs and tilts while the song plays.
        if (floatX is { } f)
        {
            var fy = Y(f) + (_motion ? Sway.Bob(_sea) : 0);
            var tilted = Draw.Turn(g, f, fy, _motion ? Sway.Tilt(_sea) : 0);
            Draw.Float(g, f, fy, 1);
            Draw.Restore(g, tilted);
        }
    }

    /// <summary>The hook over the water: from up to the right (0) to the surface above its spot (1), turning once.</summary>
    private static (float X, float Y, float Turn) Flight(float hookX, double t)
    {
        var sx = Math.Min(Panel.Width - 30, hookX + 170);
        const float sy = Panel.SeaTop - 6;
        var fx = sx + (hookX - sx) * (float)t;
        var fy = sy + (Panel.SeaTop - sy) * (float)t - (float)Math.Sin(Math.PI * t) * 48;
        return (fx, fy - 10, 360 * (float)t);
    }

    /// <summary>
    /// Where the float is drawn: where playback is, except just after a seek
    /// or a jump, when it glides there from where it was.
    /// </summary>
    private float FloatX(float target)
    {
        if (_motion && _targetX is { } last && Math.Abs(target - last) > Glide.Leap && _drawnX is { } drawn)
            (_glideFrom, _glideAt, _gliding) = (drawn - target, Stopwatch.GetTimestamp(), true);
        _targetX = target;
        var x = target;
        if (_gliding)
        {
            var t = Since(_glideAt);
            if (Glide.Settled(_glideFrom, t)) _gliding = false;
            else x = target + (float)Glide.Offset(_glideFrom, t);
        }
        _drawnX = x;
        return x;
    }

    /// <summary>Bubbles rise from the hook while the song plays.</summary>
    private void Bubbles(nint g, float hx, float hy, bool playing)
    {
        if (!_motion || !playing) return;
        var bubbles = _sea.Cycle(7_000);
        for (var k = 0; k < 3; k++)
        {
            var t = (bubbles - k * 0.09) / 0.45;
            if (t is < 0 or > 1) continue;
            var by = hy - 3 - 48 * (float)t;
            var bx = hx + 9 + (float)Math.Sin(t * 2 * Math.PI + k) * 3;
            var r = 2.2f + 0.6f * k;
            using var ring = new Draw.Pen(Sea.Faded(Sea.Ink, 0.45 * (1 - t)), 1.2f);
            GdipDrawEllipse(g, ring.Handle, bx - r, by - r, 2 * r, 2 * r);
        }
    }

    private void Times(nint g, Chart chart, long? now)
    {
        long? pointer = _pointerX is { } x ? Panel.MsAt(x, chart.DurationMs) : null;
        foreach (var mark in Panel.Times(chart.DurationMs, now, pointer))
        {
            var lead = mark.Kind is TimeKind.Pointer or TimeKind.Float;
            var font = mark.Kind switch
            {
                TimeKind.Pointer or TimeKind.Float => Fonts.PlexMedium(Panel.LeadSize),
                _ => Fonts.Plex(Panel.MarkSize),
            };
            var color = mark.Kind == TimeKind.Pointer ? Sea.Buoy : Sea.Ink;
            var box = new Box(mark.Left - 4, Panel.TimesTop + (lead ? 0 : 1), mark.Right - mark.Left + 8, 18);
            Draw.Text(g, Words.Clock(mark.Ms), font, box, color, oneLine: true, StringAlignCenter);
        }
    }

    /// <summary>What the hook is: its time, its name, and how the jump lands; a pin can be removed from here.</summary>
    private void HookText(nint g, HookWords hook, double alpha, bool settled)
    {
        if (alpha <= 0) return;
        var words = Panel.HookWords;
        if (hook.Ms is { } ms)
            Draw.Text(g, Words.Clock(ms), Fonts.PlexMedium(20), Panel.HookTime, Sea.Faded(Sea.Buoy, alpha), oneLine: true);
        else
            words = words with { X = Panel.Pad, Width = words.Right - Panel.Pad };
        Draw.Text(g, hook.Label, Fonts.Barlow(17), words with { Height = 22 }, Sea.Faded(Sea.Ink, alpha), oneLine: true);
        Draw.Text(
            g, hook.Detail, Fonts.BarlowMedium(14), words with { Y = words.Y + 21, Height = 20 },
            Sea.Faded(Sea.Ink, 0.75 * alpha), oneLine: true);
        if (!hook.Pinned) return;
        var color = settled && _hover == PanelPart.RemovePin ? Sea.Ink : Sea.Buoy;
        Draw.Text(g, "Remove pin", Fonts.Barlow(14, Fonts.Underline), Panel.RemovePin, Sea.Faded(color, alpha), oneLine: true);
        if (settled) Focus(g, PanelPart.RemovePin, Panel.RemovePin, 4);
    }

    /// <summary>A button as the phone draws it: Jump filled with ink, Pin an outline; its key under its name.</summary>
    private void Button(nint g, Box box, string label, string? keys, PanelPart part, bool filled)
    {
        var shape = Draw.Rounded(box.X, box.Y, box.Width, box.Height, 8);
        var pressed = _down == part && _hover == part;
        var hover = _hover == part;
        if (filled)
        {
            var fill = pressed ? Sea.Ink : hover ? Sea.Faded(Sea.Ink, 0.86) : Sea.Ink;
            using var brush = new Draw.Brush(fill);
            GdipFillPath(g, brush.Handle, shape);
        }
        else
        {
            if (hover || pressed)
            {
                using var brush = new Draw.Brush(pressed ? Sea.Contour : Sea.Foam);
                GdipFillPath(g, brush.Handle, shape);
            }
            using var pen = new Draw.Pen(Sea.Ink, 2);
            GdipDrawPath(g, pen.Handle, shape);
        }
        GdipDeletePath(shape);
        var ink = filled ? Sea.Foam : Sea.Ink;
        var top = keys is null ? box.Y + 9 : box.Y + 3;
        Draw.Text(g, label, Fonts.BarlowBold(21), box with { Y = top, Height = 26 }, ink, oneLine: true, StringAlignCenter);
        if (keys is not null)
        {
            Draw.Text(
                g, keys, Fonts.Plex(10), box with { Y = box.Y + 27, Height = 14 }, Sea.Faded(ink, 0.8), oneLine: true,
                StringAlignCenter);
        }
        Focus(g, part, box, 8);
    }

    /// <summary>The ring round what Tab chose, shown once the keyboard is used, as Windows does.</summary>
    private void Focus(nint g, PanelPart part, Box box, float radius)
    {
        if (!_focusShown || _focus != part) return;
        var ring = Draw.Rounded(box.X - 3, box.Y - 3, box.Width + 6, box.Height + 6, radius + 3);
        using (var pen = new Draw.Pen(Sea.Ink, 2)) GdipDrawPath(g, pen.Handle, ring);
        GdipDeletePath(ring);
    }
}
