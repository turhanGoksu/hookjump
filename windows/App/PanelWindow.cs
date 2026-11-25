using System;
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
    private const uint FrameMs = 100; // the float moves on between Spotify's reports

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
        Place(chart);
        SetForegroundWindow(_window);
        SetTimer(_window, FrameTimer, FrameMs, 0);
    }

    /// <summary>The song as it is now; a song starting or ending changes the panel's height.</summary>
    public void Update(Chart? chart)
    {
        if (!Visible) return;
        if ((chart is null) != (_chart is null)) Place(chart);
        _chart = chart;
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
                if (_chart is { Playing: true }) Invalidate();
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
        Line(g, chart, now);
        Times(g, chart, now);
        HookWords(g, chart);
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

    /// <summary>The surface and the depth contours: the sea, not data.</summary>
    private static void Water(nint g)
    {
        const float top = Panel.SeaTop, width = Panel.Width;
        using var contour = new Draw.Pen(Sea.Contour, 1.2f);
        var points = stackalloc PointF[7];
        for (var i = 0; i < 3; i++)
        {
            var cy = top + (i + 0.6f) * (Panel.SeaBottom - top) / 3;
            var a = 12f * (i % 2 == 0 ? 1 : -1);
            points[0] = new(-20, cy);
            points[1] = new(width * 0.2f, cy - a);
            points[2] = new(width * 0.4f, cy + a);
            points[3] = new(width * 0.62f, cy);
            points[4] = new(width * 0.8f, cy - a * 0.8f);
            points[5] = new(width * 0.92f, cy + a * 0.4f);
            points[6] = new(width + 20, cy - a * 0.2f);
            GdipCreatePath(0, out var path);
            GdipAddPathBeziers(path, points, 7);
            GdipDrawPath(g, contour.Handle, path);
            GdipDeletePath(path);
        }
        // The surface: its waves, half a step up, half a step down.
        GdipCreatePath(0, out var surface);
        const float step = 32, rise = 5;
        var up = true;
        for (var sx = -step; sx < width + step; sx += step)
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

    private void Line(nint g, Chart chart, long? now)
    {
        const float y = Panel.LineY, left = Panel.LineLeft, right = Panel.LineRight;
        float X(long ms) => Panel.X(ms, chart.DurationMs);
        var hookX = chart.HookMs is { } hookMs ? X(hookMs) : (float?)null;

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
            GdipAddPathLine(path, left, Panel.SeaTop, left, y - 8);
            GdipAddPathArc(path, left, y - 16, 16, 16, 180, -90);
            GdipAddPathLine(path, left + 8, y, hookX ?? right, y);
            GdipDrawPath(g, ink.Handle, path);
            GdipDeletePath(path);
        }
        if (hookX is { } from && from < right)
        {
            using var dots = new Draw.Pen(Sea.Ink, 2);
            var dashes = stackalloc float[] { 1.5f, 3.5f }; // in line widths: 3 px dots, 7 px apart
            GdipSetPenDashArray(dots.Handle, dashes, 2);
            GdipDrawLine(g, dots.Handle, from, y, right, y);
        }

        // Where the listener rewound to (above) and skipped to (below).
        using (var rewound = new Draw.Pen(Sea.Ink, 3))
        {
            foreach (var ms in chart.Rewinds) GdipDrawLine(g, rewound.Handle, X(ms), y - 10, X(ms), y - 30);
        }
        using (var skipped = new Draw.Pen(Sea.Kelp, 3))
        {
            foreach (var ms in chart.Skips) GdipDrawLine(g, skipped.Handle, X(ms), y + 10, X(ms), y + 24);
        }

        // Where a click would pin.
        if (_pointerX is { } pointer)
        {
            using var mark = new Draw.Pen(Sea.Faded(Sea.Buoy, 0.6), 1.5f);
            GdipDrawLine(g, mark.Handle, pointer, Panel.SeaTop + 8, pointer, Panel.SeaBottom - 10);
        }

        var floatX = now is { } at ? X(at) : (float?)null;
        if (hookX is { } hx)
        {
            // The hook hangs from the line; under the float, it hangs from the float.
            var under = floatX is { } fx ? Math.Clamp(1 - Math.Abs(fx - hx) / 20, 0, 1) : 0;
            Draw.Hook(g, hx, y + 3 + 15 * under, 1);
        }
        if (floatX is { } f) Draw.Float(g, f, y, 1);
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

    private void HookWords(nint g, Chart chart)
    {
        var words = Panel.HookWords;
        if (chart.HookMs is { } hook)
            Draw.Text(g, Words.Clock(hook), Fonts.PlexMedium(20), Panel.HookTime, Sea.Buoy, oneLine: true);
        else
            words = words with { X = Panel.Pad, Width = words.Right - Panel.Pad };
        Draw.Text(g, chart.HookLabel, Fonts.Barlow(17), words with { Height = 22 }, Sea.Ink, oneLine: true);
        Draw.Text(
            g, chart.HookDetail, Fonts.BarlowMedium(14), words with { Y = words.Y + 21, Height = 20 },
            Sea.Faded(Sea.Ink, 0.75), oneLine: true);
        if (chart.Pinned)
        {
            var color = _hover == PanelPart.RemovePin ? Sea.Ink : Sea.Buoy;
            Draw.Text(g, "Remove pin", Fonts.Barlow(14, Fonts.Underline), Panel.RemovePin, color, oneLine: true);
            Focus(g, PanelPart.RemovePin, Panel.RemovePin, 4);
        }
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
