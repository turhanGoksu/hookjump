using System;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// Where the strip and the panel go on a screen, in its own pixels: each
/// screen has its own scale, and a laptop with a monitor plugged in has two.
/// </summary>
public static class Placement
{
    /// <summary>How many of a screen's pixels a size takes, given in pixels at 100 %.</summary>
    public static int Pixels(float logical, uint dpi) => (int)MathF.Ceiling(logical * dpi / 96f);

    /// <summary>The gap from the edges of the work area: 12 pixels at 100 %.</summary>
    public static int Margin(uint dpi) => (int)(12 * dpi / 96f);

    /// <summary>The top left of a box in the bottom-right corner of a work area, by the clock.</summary>
    public static (int X, int Y) Corner(int workRight, int workBottom, int width, int height, uint dpi) =>
        (workRight - width - Margin(dpi), workBottom - height - Margin(dpi));
}

/// <summary>The screens, as Windows tells them.</summary>
internal static unsafe class Screen
{
    /// <summary>The screen the listener works on: the one with the window in front, or else the pointer.</summary>
    public static nint Working()
    {
        var front = GetForegroundWindow();
        if (front != 0) return MonitorFromWindow(front, MonitorDefaultToNearest);
        Point pointer;
        GetCursorPos(&pointer);
        return MonitorFromPoint(pointer, MonitorDefaultToNearest);
    }

    /// <summary>The main screen, where Windows 11 keeps the notification area and the hook.</summary>
    public static nint Primary() => MonitorFromPoint(new Point(), MonitorDefaultToPrimary);

    /// <summary>The screen a point is on, or the nearest one.</summary>
    public static nint At(Point point) => MonitorFromPoint(point, MonitorDefaultToNearest);

    /// <summary>A screen's work area (without the taskbar) and its scale, as dots per inch (96 is 100 %).</summary>
    public static (Rect Work, uint Dpi) Of(nint monitor)
    {
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        GetMonitorInfo(monitor, &info);
        // The scale the listener set for that screen (MDT_EFFECTIVE_DPI).
        return GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? (info.Work, dpi) : (info.Work, 96);
    }
}
