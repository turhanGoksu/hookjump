using System.Collections.Generic;

namespace Hookjump.Core;

/// <summary>
/// Turn polled player positions into listening events (spec/ALGORITHM.md, section 3).
/// </summary>
/// <remarks>
/// Between two samples of the same track, the position should advance by about
/// the time that passed; anything far from that is the listener moving the
/// playhead.
/// </remarks>
public static class Seek
{
    public const long ToleranceMs = 2000; // polling jitter; smaller moves are not seeks
    public const long MaxGapMs = 5000; // a longer gap between samples (sleep, a hang) says nothing
    public const long RestartMs = 3000; // going back to before this is starting the track over

    /// <summary>Listening events, in order, from samples in time order.</summary>
    public static List<ListeningEvent> Detect(IEnumerable<PlaybackSample> samples)
    {
        var events = new List<ListeningEvent>();
        var listen = 0;
        Known? prev = null;
        foreach (var sample in samples)
        {
            if (sample.TrackId is not { } trackId || sample.PositionMs is not { } position) continue;
            var now = new Known(trackId, sample.AtMs, sample.Playing, position);
            var before = prev;
            prev = now;
            if (before is null || trackId != before.TrackId)
            {
                listen += 1;
                continue;
            }

            var elapsed = now.AtMs - before.AtMs;
            var moved = position - before.PositionMs;
            if (sample.OwnSeek || elapsed <= 0 || elapsed > MaxGapMs) continue;

            // Playing during the whole interval: the position moves by `elapsed`.
            // Playing during part of it (paused or resumed in between): by anything
            // from 0 to `elapsed`. Paused throughout: not at all.
            var both = before.Playing && now.Playing;
            var either = before.Playing || now.Playing;
            var low = (both ? elapsed : 0) - ToleranceMs;
            var high = (either ? elapsed : 0) + ToleranceMs;
            if (low <= moved && moved <= high)
            {
                if (moved > 0) AddPlay(events, trackId, listen, before.PositionMs, moved);
            }
            else if (moved < 0 && position < RestartMs)
            {
                listen += 1;
            }
            else
            {
                events.Add(new SeekEvent(trackId, listen, before.PositionMs, position));
            }
        }
        return events;
    }

    /// <summary>A sample with both a track and a position: the only kind compared.</summary>
    private sealed record Known(string TrackId, long AtMs, bool Playing, long PositionMs);

    private static void AddPlay(List<ListeningEvent> events, string trackId, int listen, long fromMs, long moved)
    {
        // Extend the last span when this one carries straight on from it, as after
        // a pause; otherwise start a new span.
        if (events.Count > 0 && events[^1] is PlaySpan last &&
            last.ToMs == fromMs && last.TrackId == trackId && last.Listen == listen)
        {
            events[^1] = last with { ToMs = fromMs + moved };
            return;
        }
        events.Add(new PlaySpan(trackId, listen, fromMs, fromMs + moved));
    }
}
