using System;
using System.Collections.Generic;
using System.Linq;
using Hookjump.Core;
using Hookjump.Player;

namespace Hookjump;

/// <summary>
/// The playing song as plain data for a drawing: the counterpart of the Mac's
/// record sleeve (src/hookjump/sleeve.py) and the phone's fishing line.
/// </summary>
/// <remarks>
/// Where playback is, where the jump goes, where the listener rewound to and
/// skipped to. Drawing it is the app's work; this only decides what goes where.
/// </remarks>
public sealed record Chart(
    string Title,
    string? Artist,
    long DurationMs,
    long? PositionMs, // where playback was at PositionAtMs
    long PositionAtMs,
    bool Playing,
    long? HookMs, // the pin or the learned spot, before any lead-in
    bool Pinned, // the hook is a pin, which the listener can remove
    string HookLabel, // what the hook is, or what to do when there is none
    string HookDetail,
    IReadOnlyList<long> Rewinds, // where the listener rewound to
    IReadOnlyList<long> Skips, // where they skipped forward to
    bool Ignored = false, // the listener chose not to track this song
    bool LearningPaused = false) // nothing is being learned from any song
{
    /// <summary>Where playback is at <paramref name="clockMs"/>, moving on while it plays.</summary>
    public long? NowAt(long clockMs)
    {
        if (PositionMs is not { } position) return null;
        if (!Playing || clockMs <= PositionAtMs) return position;
        return Math.Min(DurationMs, position + (clockMs - PositionAtMs));
    }

    /// <summary>Null when there is no track with a known length to draw.</summary>
    public static Chart? For(
        Reading? reading,
        Pin? pin,
        Favorite? favorite,
        IReadOnlyCollection<SeekEvent> seeks,
        bool ignored = false,
        bool learningPaused = false)
    {
        if (reading?.TrackId is null || reading.DurationMs is not { } duration || duration <= 0) return null;
        var (hookMs, label, detail) = (pin, favorite) switch
        {
            ({ } p, _) => ((long?)p.AtMs, "Your pinned spot",
                p.Typed ? "the jump lands right there" : "the jump lands 1.5 s before it"),
            (_, { } f) => (f.TargetMs, "The part you replay most", $"learned from {f.Listens} listens"),
            // With no hook, say why: learning may be off for this song or for all.
            _ when ignored => (null, "Not learning from this song", "a pin still works"),
            _ when learningPaused => (null, "Learning is paused", "a pin still works"),
            _ => (null, "Nothing learned yet", "press Pin at the part you love"),
        };
        return new Chart(
            Title: reading.Title ?? reading.Artist ?? "Unknown track",
            Artist: reading.Title is null ? null : reading.Artist,
            DurationMs: duration,
            PositionMs: reading.PositionMs,
            PositionAtMs: reading.AtMs,
            Playing: reading.Playing,
            HookMs: hookMs,
            Pinned: pin is not null,
            HookLabel: label,
            HookDetail: detail,
            Rewinds: seeks.Where(s => s.ToMs < s.FromMs).Select(s => s.ToMs).ToList(),
            Skips: seeks.Where(s => s.ToMs >= s.FromMs).Select(s => s.ToMs).ToList(),
            Ignored: ignored,
            LearningPaused: learningPaused);
    }

    /// <summary>Where <paramref name="ms"/> sits along the line, from 0.0 at the start to 1.0 at the end.</summary>
    public static double Fraction(long ms, long durationMs) => Math.Clamp((double)ms / durationMs, 0.0, 1.0);

    /// <summary>The moment under a point of the line, to the whole second.</summary>
    public static long MsAt(double fraction, long durationMs)
    {
        // Half a second rounds up, as Kotlin's roundToLong does.
        var ms = (long)Math.Floor(Math.Clamp(fraction, 0.0, 1.0) * durationMs / 1000 + 0.5) * 1000;
        return Math.Min(ms, durationMs);
    }
}
