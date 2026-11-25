using Hookjump.Player;

namespace Hookjump.App;

/// <summary>What a time typed for a pin comes to: the time to pin, or why not.</summary>
/// <param name="AtMs">The time to pin, exact; null when there is a problem.</param>
/// <param name="Problem">Why nothing can be pinned, said in the dialog so it can be fixed there.</param>
public sealed record TypedPin(long? AtMs, string? Problem)
{
    /// <summary>
    /// Checks a typed time against the song the dialog was opened for, and
    /// the song playing now: a time for one song is never pinned on another.
    /// </summary>
    public static TypedPin Check(string text, string askedTrack, Reading? now)
    {
        if (now?.TrackId != askedTrack) return new(null, Words.SongChangedBeforePin);
        var ms = Words.ParseClock(text);
        // A song of unknown length takes any time, as the Mac does.
        if (Words.TypedTimeProblem(ms, now.DurationMs ?? long.MaxValue) is { } problem) return new(null, problem);
        return new(ms, null);
    }
}
