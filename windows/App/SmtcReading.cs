using System;
using Hookjump.Player;

namespace Hookjump.App;

/// <summary>What Windows' media controls say, as a <see cref="Reading"/>.</summary>
/// <remarks>Kept apart from the Windows calls, so it is tested without a player.</remarks>
public static class SmtcReading
{
    /// <summary>
    /// SMTC times are <see cref="TimeSpan"/> ticks of 100 ns; the core counts
    /// integer milliseconds, rounded as spec/ALGORITHM.md section 1 says.
    /// </summary>
    public static long Ms(long ticks) => (ticks + 5000) / 10000;

    public static Reading From(
        string? title,
        string? artist,
        string? album,
        int trackNumber,
        bool playing,
        long positionTicks,
        long startTicks,
        long endTicks,
        DateTimeOffset lastUpdated,
        long nowMs)
    {
        var trackId = TrackKey.Of(title, artist, album, trackNumber);
        // The moment Windows measured the position; one never measured is now.
        var atMs = lastUpdated.ToUnixTimeMilliseconds();
        if (atMs <= 0) atMs = nowMs;
        if (trackId is null)
        {
            // Spotify with nothing loaded still says 0:00: that is not a position.
            return new Reading(atMs, null, null, null, null, playing, null);
        }
        return new Reading(
            atMs,
            trackId,
            title,
            string.IsNullOrEmpty(artist) ? null : artist,
            endTicks > startTicks ? Ms(endTicks - startTicks) : null,
            playing,
            positionTicks >= 0 ? Ms(positionTicks) : null);
    }
}
