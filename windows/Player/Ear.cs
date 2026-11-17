using System;
using System.Collections.Generic;
using Hookjump.Core;

namespace Hookjump.Player;

/// <summary>Turns the player's reports into the samples seek detection reads.</summary>
/// <remarks>
/// <para>
/// Seek detection (spec/ALGORITHM.md, section 3) expects samples about a second
/// apart. The player tells about changes instead, so for each report that says
/// something new the ear writes two samples: one just before it, where
/// playback would have been had nothing happened, and the report itself. A
/// seek then shows up between the two, at its exact moment.
/// </para>
/// <para>
/// Two habits of Spotify, seen on Android, shape what counts as new:
/// before a track has played, its position may be a placeholder (on start
/// Spotify reports 0:00 and then where the track was left), so a track is
/// trusted from its first report while playing; and around a start or a seek
/// it reports many times a second, each matching playback, so a report within
/// <see cref="QuietMs"/> of where playback would be anyway adds nothing.
/// On Windows Spotify also repeats its position every few seconds; those
/// reports match playback too.
/// </para>
/// <para>
/// Reports are dated by the player, so one taken in late still lands at its
/// own moment; one said before the last report heard adds nothing.
/// </para>
/// </remarks>
public sealed class Ear
{
    public const long QuietMs = 250;

    private Reading? _last; // the latest trusted report, written or not
    private bool _silent; // the last thing heard was "no player"
    private string? _trusted; // the track that has played since it came
    private bool _ownMove;

    /// <summary>hookjump moved the playhead: mark the next sample as its own.</summary>
    public void HeardOwnMove() => _ownMove = true;

    /// <summary>Samples for a report, or none if it says nothing new. <paramref name="nowMs"/> dates "no player".</summary>
    public List<PlaybackSample> Hear(Reading? reading, long nowMs)
    {
        if (reading is null)
        {
            if (_silent) return [];
            _silent = true;
            _last = null;
            _trusted = null;
            return [new PlaybackSample(nowMs, null, false, null, TakeOwnMove())];
        }
        // Overtaken: a report said before the last one heard, taken in late.
        if (_last is not null && reading.AtMs < _last.AtMs) return [];
        _silent = false;
        var trackId = reading.TrackId;
        if (trackId is not null && trackId != _trusted)
        {
            if (!reading.Playing) return []; // not played yet: maybe a placeholder
            _trusted = trackId;
        }
        var prev = _last;
        _last = reading;
        if (prev is not null && SaysNothingNew(prev, reading)) return [];

        var samples = new List<PlaybackSample>();
        var justBefore = reading.AtMs - 1;
        if (prev is { TrackId: not null, PositionMs: not null } && prev.TrackId == trackId && justBefore > prev.AtMs)
            samples.Add(new PlaybackSample(justBefore, prev.TrackId, prev.Playing, Expected(prev, justBefore)));
        samples.Add(new PlaybackSample(reading.AtMs, trackId, reading.Playing, reading.PositionMs, TakeOwnMove()));
        return samples;
    }

    private bool TakeOwnMove()
    {
        var own = _ownMove;
        _ownMove = false;
        return own;
    }

    /// <summary>Where <paramref name="prev"/> says playback is at <paramref name="atMs"/>, had nothing happened since.</summary>
    private static long Expected(Reading prev, long atMs)
    {
        var position = prev.PositionMs ?? throw new InvalidOperationException("no position to go on from");
        return prev.Playing ? position + (atMs - prev.AtMs) : position;
    }

    private static bool SaysNothingNew(Reading prev, Reading reading)
    {
        if (prev == reading) return true;
        if (prev.TrackId != reading.TrackId || prev.Playing != reading.Playing) return false;
        if (reading.TrackId is null) return true; // still nothing loaded
        if (reading.PositionMs is not { } position) return prev.PositionMs is null;
        if (prev.PositionMs is null) return false;
        return Math.Abs(position - Expected(prev, reading.AtMs)) <= QuietMs;
    }
}
