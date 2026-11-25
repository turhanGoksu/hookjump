using System;
using Hookjump.Player;
using Xunit;

namespace Hookjump.App.Tests;

/// <summary>What Windows' media controls say, turned into a reading.</summary>
public class SmtcReadingTest
{
    private const long Now = 1_700_000_000_000;
    private static readonly DateTimeOffset Measured = DateTimeOffset.FromUnixTimeMilliseconds(Now - 2000);

    private static Reading Spotify(
        string title = "KARANLIK",
        bool playing = true,
        TimeSpan? position = null,
        TimeSpan? end = null,
        DateTimeOffset? lastUpdated = null) =>
        SmtcReading.From(
            title, "Yung Kafa & Kücük Efendi", "KARANLIK", 1, playing,
            (position ?? TimeSpan.FromMilliseconds(15024)).Ticks, 0, (end ?? TimeSpan.FromMilliseconds(174678)).Ticks,
            lastUpdated ?? Measured, Now);

    [Fact]
    public void APlayingSongIsReadAsItWasMeasured()
    {
        Assert.Equal(
            new Reading(Now - 2000, TrackKey.Of("KARANLIK", "Yung Kafa & Kücük Efendi", "KARANLIK", 1),
                "KARANLIK", "Yung Kafa & Kücük Efendi", 174678, true, 15024),
            Spotify());
    }

    [Fact]
    public void TicksRoundToTheNearestMillisecond()
    {
        Assert.Equal(0, SmtcReading.Ms(4999));
        Assert.Equal(1, SmtcReading.Ms(5000)); // half a millisecond rounds up, as the spec says
        Assert.Equal(60000, SmtcReading.Ms(TimeSpan.FromMinutes(1).Ticks));
    }

    [Fact]
    public void NothingLoadedIsNoTrackAndNoPosition()
    {
        // Spotify open with no song: an empty title at 0:00, which is no position.
        Assert.Equal(
            new Reading(Now - 2000, null, null, null, null, false, null),
            Spotify(title: "", playing: false, position: TimeSpan.Zero, end: TimeSpan.Zero));
    }

    [Fact]
    public void APausedSongIsNotPlaying()
    {
        Assert.False(Spotify(playing: false).Playing);
    }

    [Fact]
    public void ADurationIsKnownOnlyWhenTheTimelineHasOne()
    {
        Assert.Null(Spotify(end: TimeSpan.Zero).DurationMs);
    }

    [Fact]
    public void APositionNeverMeasuredIsDatedNow()
    {
        Assert.Equal(Now, Spotify(lastUpdated: DateTimeOffset.MinValue).AtMs);
    }

    [Fact]
    public void OnlySpotifyIsRead()
    {
        Assert.True(SmtcTurntable.IsSpotify("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")); // Microsoft Store
        Assert.True(SmtcTurntable.IsSpotify("Spotify.exe")); // from spotify.com
        Assert.False(SmtcTurntable.IsSpotify("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"));
        Assert.False(SmtcTurntable.IsSpotify("MSEdge"));
    }
}
