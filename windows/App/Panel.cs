using System;
using System.Collections.Generic;
using System.Linq;

namespace Hookjump.App;

/// <summary>A part of the panel that takes a click or a key; None is the rest.</summary>
public enum PanelPart
{
    None,
    Line,
    RemovePin,
    Previous,
    PlayPause,
    Next,
    Jump,
    Pin,
}

/// <summary>A rectangle of the panel, in pixels at 100 % scale.</summary>
public readonly record struct Box(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>What a time under the line is: where playback is, where the pointer would pin, or a mark of the line.</summary>
public enum TimeKind
{
    Pointer,
    Float,
    Start,
    End,
    Minute,
}

/// <summary>A time written under the line, from Left to Right.</summary>
public readonly record struct TimeMark(long Ms, TimeKind Kind, float Left, float Right);

/// <summary>
/// The sea panel's grid and what is where on it, as data: PanelWindow draws
/// it, and it is tested without a screen. The song runs along a line through
/// the water, as on the strip and the Mac's menu: where the listener rewound
/// to above it, where they skipped to below it, the hook hanging from it.
/// </summary>
public static class Panel
{
    public const float Width = 460, Pad = 20;
    public const float FullHeight = 398, MessageHeight = 112;

    // The water and the line through it.
    public const float SeaTop = 80, SeaBottom = 194, LineY = 134, LineLeft = 30, LineRight = 436;

    /// <summary>How far above or below the line a click still pins.</summary>
    public const float Reach = 18;

    public const float TimesTop = 197;

    // Plex Mono is monospaced: each character is 0.6 of the size wide.
    public const float MarkSize = 11, LeadSize = 12;

    public static readonly Box Title = new(Pad, 12, Width - 2 * Pad, 34);
    public static readonly Box Artist = new(Pad, 46, Width - 2 * Pad, 22);
    public static readonly Box HookTime = new(Pad, 222, 62, 28);
    public static readonly Box HookWords = new(Pad + 64, 222, 206, 40);
    public static readonly Box RemovePin = new(Pad + 64, 262, 84, 20);
    public static readonly Box Answer = new(300, 224, Width - 300 - Pad, 58);

    // The song buttons, round, in the middle, as on the phone.
    public const float SongButton = 40, SongGap = 28;
    public static readonly Box Previous = new(Width / 2 - SongButton * 1.5f - SongGap, 288, SongButton, SongButton);
    public static readonly Box PlayPause = new(Width / 2 - SongButton / 2, 288, SongButton, SongButton);
    public static readonly Box Next = new(Width / 2 + SongButton / 2 + SongGap, 288, SongButton, SongButton);

    public static readonly Box Jump = new(Pad, 338, (Width - 2 * Pad - 12) / 2, 44);
    public static readonly Box Pin = new(Pad + (Width - 2 * Pad - 12) / 2 + 12, 338, (Width - 2 * Pad - 12) / 2, 44);

    /// <summary>With nothing playing, the panel only says so, and is short.</summary>
    public static float HeightFor(bool hasChart) => hasChart ? FullHeight : MessageHeight;

    /// <summary>Where a moment of the song is along the line.</summary>
    public static float X(long ms, long durationMs) =>
        LineLeft + (LineRight - LineLeft) * (float)Chart.Fraction(ms, durationMs);

    /// <summary>The moment under a point of the line, to the whole second, as on the phone and the Mac.</summary>
    public static long MsAt(float x, long durationMs) => Chart.MsAt((x - LineLeft) / (LineRight - LineLeft), durationMs);

    /// <summary>What is under the pointer.</summary>
    public static PanelPart Hit(float x, float y, bool hasChart, bool pinned)
    {
        if (!hasChart) return PanelPart.None;
        if (x >= LineLeft - 6 && x <= LineRight + 6 && Math.Abs(y - LineY) <= Reach) return PanelPart.Line;
        if (pinned && RemovePin.Contains(x, y)) return PanelPart.RemovePin;
        if (Round(Previous, x, y)) return PanelPart.Previous;
        if (Round(PlayPause, x, y)) return PanelPart.PlayPause;
        if (Round(Next, x, y)) return PanelPart.Next;
        if (Jump.Contains(x, y)) return PanelPart.Jump;
        if (Pin.Contains(x, y)) return PanelPart.Pin;
        return PanelPart.None;
    }

    /// <summary>Where Tab (or Shift+Tab, backwards) goes next: through what can be pressed, round and round.</summary>
    public static PanelPart NextFocus(PanelPart current, bool backwards, bool hasChart, bool pinned)
    {
        if (!hasChart) return PanelPart.None;
        PanelPart[] order = pinned
            ? [PanelPart.RemovePin, PanelPart.Previous, PanelPart.PlayPause, PanelPart.Next, PanelPart.Jump, PanelPart.Pin]
            : [PanelPart.Previous, PanelPart.PlayPause, PanelPart.Next, PanelPart.Jump, PanelPart.Pin];
        var at = Array.IndexOf(order, current);
        if (at < 0) return backwards ? order[^1] : order[0];
        return order[(at + (backwards ? order.Length - 1 : 1)) % order.Length];
    }

    /// <summary>
    /// The times written under the line. The pointer's time comes first while
    /// it is over the line, then where playback is, then the line's start and
    /// end, then the minutes; each makes way for those before it.
    /// </summary>
    public static List<TimeMark> Times(long durationMs, long? floatMs, long? pointerMs)
    {
        var marks = new List<TimeMark>();
        void Add(long ms, TimeKind kind)
        {
            var size = kind is TimeKind.Pointer or TimeKind.Float ? LeadSize : MarkSize;
            var width = Words.Clock(ms).Length * 0.6f * size;
            var x = kind switch
            {
                TimeKind.Start => LineLeft,
                TimeKind.End => LineRight - width,
                _ => Math.Clamp(X(ms, durationMs) - width / 2, Pad / 2, Width - Pad / 2 - width),
            };
            const float gap = 8;
            if (marks.Any(m => x < m.Right + gap && m.Left < x + width + gap)) return;
            marks.Add(new TimeMark(ms, kind, x, x + width));
        }
        if (pointerMs is { } pointer) Add(pointer, TimeKind.Pointer);
        if (floatMs is { } now) Add(now, TimeKind.Float);
        Add(0, TimeKind.Start);
        Add(durationMs, TimeKind.End);
        for (var minute = 60_000L; minute < durationMs; minute += 60_000) Add(minute, TimeKind.Minute);
        return marks;
    }

    /// <summary>Inside a round button: its corners take no click.</summary>
    private static bool Round(Box box, float x, float y)
    {
        var (dx, dy) = (x - (box.X + box.Width / 2), y - (box.Y + box.Height / 2));
        return dx * dx + dy * dy <= box.Width * box.Width / 4;
    }

    /// <summary>
    /// A click on the hook while the panel is open closes it, and Windows then
    /// tells of the same click: that one must not open it again.
    /// </summary>
    public static bool OpensOnClick(long nowMs, long closedAtMs) => nowMs - closedAtMs > 300;
}
