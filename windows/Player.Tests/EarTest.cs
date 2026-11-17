using System.Collections.Generic;
using System.Linq;
using Hookjump.Core;
using Xunit;

namespace Hookjump.Player.Tests;

public class EarTest
{
    private const long T0 = 1_700_000_000_000;

    private readonly Ear _ear = new();

    private static Reading At(long atMs, long? positionMs, string? track = "a", bool playing = true) =>
        new(atMs, track, "Title", "Artist", 240000, playing, positionMs);

    private static List<SeekEvent> Seeks(IEnumerable<PlaybackSample> samples) =>
        Seek.Detect(samples).OfType<SeekEvent>().ToList();

    [Fact]
    public void AFirstReportBecomesOneSample()
    {
        Assert.Equal([new PlaybackSample(T0, "a", true, 30000)], _ear.Hear(At(T0, 30000), T0));
    }

    [Fact]
    public void AReportHeardAgainAddsNothing()
    {
        _ear.Hear(At(T0, 30000), T0);
        Assert.Empty(_ear.Hear(At(T0, 30000), T0 + 30000));
    }

    [Fact]
    public void AReportThatMatchesPlaybackAddsNothing()
    {
        // Spotify reports many times a second as a track starts, and on Windows
        // repeats its position every few seconds.
        _ear.Hear(At(T0, 90), T0);
        Assert.Empty(_ear.Hear(At(T0 + 430, 520), T0 + 430));
        Assert.Empty(_ear.Hear(At(T0 + 910, 1000 + 200), T0 + 910));
        Assert.Empty(_ear.Hear(At(T0 + 60000, 60090, playing: true), T0 + 60000));
    }

    [Fact]
    public void AReportOffFromPlaybackGetsASampleJustBeforeItWherePlaybackWouldHaveBeen()
    {
        _ear.Hear(At(T0, 30000), T0);
        _ear.Hear(At(T0 + 430, 30430), T0 + 430); // quiet, but the newest base
        Assert.Equal(
            [new PlaybackSample(T0 + 9999, "a", true, 39999), new PlaybackSample(T0 + 10000, "a", true, 45000)],
            _ear.Hear(At(T0 + 10000, 45000), T0 + 10000));
    }

    [Fact]
    public void ATrackThatHasNotPlayedYetIsNotTrusted()
    {
        // Spotify starting: the last track loads at 0:00, then where it was
        // left, both paused. That is no seek.
        Assert.Empty(_ear.Hear(At(T0, 0, playing: false), T0));
        Assert.Empty(_ear.Hear(At(T0 + 1600, 102240, playing: false), T0 + 1600));
        var played = _ear.Hear(At(T0 + 9000, 102240), T0 + 9000);
        Assert.Equal([new PlaybackSample(T0 + 9000, "a", true, 102240)], played);
        Assert.Empty(Seeks(played));
    }

    [Fact]
    public void WhilePausedThePositionStays()
    {
        _ear.Hear(At(T0, 30000), T0);
        _ear.Hear(At(T0 + 1000, 31000, playing: false), T0 + 1000);
        Assert.Empty(_ear.Hear(At(T0 + 20000, 31000, playing: false), T0 + 20000));
        Assert.Equal(
            new PlaybackSample(T0 + 60999, "a", false, 31000),
            _ear.Hear(At(T0 + 61000, 31000), T0 + 61000).First());
    }

    [Fact]
    public void ANewTrackHasNoSampleBeforeIt()
    {
        _ear.Hear(At(T0, 30000), T0);
        Assert.Equal([new PlaybackSample(T0 + 5000, "b", true, 0)], _ear.Hear(At(T0 + 5000, 0, "b"), T0 + 5000));
    }

    [Fact]
    public void AClosedPlayerIsHeardOnce()
    {
        _ear.Hear(At(T0, 30000), T0);
        Assert.Equal([new PlaybackSample(T0 + 1, null, false, null)], _ear.Hear(null, T0 + 1));
        Assert.Empty(_ear.Hear(null, T0 + 2));
    }

    [Fact]
    public void APlayerWithNothingLoadedIsHeardOnce()
    {
        Assert.Single(_ear.Hear(At(T0, null, track: null, playing: false), T0));
        Assert.Empty(_ear.Hear(At(T0 + 500, null, track: null, playing: false), T0 + 500));
    }

    [Fact]
    public void ARewindReportedLaterIsStillFoundAtItsExactMoment()
    {
        // Played from 0:30 for 70 s, then rewound to 1:00: the report, even one
        // picked up late, says when it happened.
        var samples = _ear.Hear(At(T0, 30000), T0).Concat(_ear.Hear(At(T0 + 70000, 60000), T0 + 95000));
        Assert.Equal([new SeekEvent("a", 1, 99999, 60000)], Seeks(samples));
    }

    [Fact]
    public void AReportSaidBeforeTheLastOneHeardAddsNothing()
    {
        // A check read the player while older reports still waited.
        _ear.Hear(At(T0, 30000), T0);
        _ear.Hear(At(T0 + 5000, 100000), T0 + 5000);
        Assert.Empty(_ear.Hear(At(T0 + 3000, 60000), T0 + 5001));
    }

    [Fact]
    public void UntouchedPlaybackReportedNowAndThenIsNoSeek()
    {
        var samples = _ear.Hear(At(T0, 30000), T0);
        for (var i = 1; i <= 10; i++)
            samples.AddRange(_ear.Hear(At(T0 + i * 30000, 30000 + i * 30000L + 300), T0 + i * 30000));
        Assert.Empty(Seeks(samples));
    }

    [Fact]
    public void HookjumpsOwnMoveIsMarkedOnTheNextSampleOnly()
    {
        var first = _ear.Hear(At(T0, 30000), T0);
        _ear.HeardOwnMove();
        Assert.Empty(_ear.Hear(At(T0, 30000), T0)); // nothing new: the mark waits
        var moved = _ear.Hear(At(T0 + 1000, 100000), T0 + 1000);
        Assert.Equal([false, true], moved.Select(s => s.OwnSeek));
        Assert.False(_ear.Hear(At(T0 + 2000, 101500), T0 + 2000).Last().OwnSeek);
        Assert.Empty(Seeks(first.Concat(moved)));
    }
}
