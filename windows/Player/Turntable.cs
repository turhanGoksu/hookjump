namespace Hookjump.Player;

// The turntable: what every player adapter must do, and nothing more.
//
// Windows' media controls (SMTC), like Android's media sessions, report the
// player's state when it changes, and each report says when its position was
// measured. So a reading carries its own moment. Every turntable keeps three
// promises:
//
// 1. Read() returns one report: the track, the state and the position belong
//    to its AtMs.
// 2. Place() moves the playhead only if the given track is still loaded.
// 3. A value the player does not report is null, never a made-up 0.

/// <summary>One report of the player. Null means the player did not say.</summary>
public sealed record Reading(
    long AtMs, // Unix time when the player measured this position
    string? TrackId, // null: nothing loaded
    string? Title,
    string? Artist,
    long? DurationMs,
    bool Playing,
    long? PositionMs);

public interface ITurntable
{
    /// <summary>For messages, such as "Spotify".</summary>
    string Name { get; }

    /// <summary>The player's latest report, or null if the player is not running.</summary>
    Reading? Read();

    /// <summary>
    /// Ask the player to move the playhead if <paramref name="trackId"/> is
    /// still loaded. False if it is not; true only means the request was taken,
    /// as the player reports where it went later.
    /// </summary>
    bool Place(string trackId, long positionMs);
}
