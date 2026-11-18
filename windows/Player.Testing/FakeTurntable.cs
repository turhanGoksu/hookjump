namespace Hookjump.Player.Testing;

/// <summary>
/// A player in memory that confirms moves later, as Spotify does through
/// Windows' media controls: Place() records the request, and Report() is the
/// player's next report.
/// </summary>
public sealed class FakeTurntable(string? track = "a", long? positionMs = 30000) : ITurntable
{
    public string Name => "Fake";
    public string? Track { get; set; } = track;
    public long? PositionMs { get; set; } = positionMs;
    public bool Running { get; set; } = true;
    public bool Playing { get; set; } = true;
    public long AtMs { get; set; } = 1_700_000_000_000;
    public int Ignores { get; set; } // how many coming moves to accept but not do
    public string? SwitchTo { get; set; } // the listener changes track first
    public int Places { get; private set; }

    public Reading? Read() =>
        Running ? new Reading(AtMs, Track, "Title", "Artist", 240000, Playing, PositionMs) : null;

    public bool Place(string trackId, long positionMs)
    {
        Places += 1;
        if (SwitchTo is { } next)
        {
            Track = next;
            PositionMs = 0;
        }
        if (!Running || Track != trackId) return false;
        if (Ignores > 0) Ignores -= 1;
        else PositionMs = positionMs;
        return true;
    }

    /// <summary>The player's next report, <paramref name="afterMs"/> later.</summary>
    public Reading? Report(long afterMs = 150)
    {
        AtMs += afterMs;
        return Read();
    }
}
