using Xunit;

namespace Hookjump.App.Tests;

/// <summary>The track id of spec/STORAGE.md, "Track ids on Windows".</summary>
public class TrackKeyTest
{
    [Fact]
    public void ASongGetsTheIdTheSpecGives()
    {
        // Worked out apart from this code, with Python's hashlib, from the
        // rule in spec/STORAGE.md.
        Assert.Equal(
            "smtc:90bab1845d10450cde71f4f60621f849ab04dc2c844ee3ff1659d6a69d715518",
            TrackKey.Of("KARANLIK", "Yung Kafa & Kücük Efendi", "KARANLIK", 1));
    }

    [Fact]
    public void TheSameSongGetsTheSameIdEveryTime()
    {
        Assert.Equal(
            TrackKey.Of("The Emptiness Machine", "Linkin Park", "From Zero", 2),
            TrackKey.Of("The Emptiness Machine", "Linkin Park", "From Zero", 2));
    }

    [Fact]
    public void EveryFieldTellsSongsApart()
    {
        var song = TrackKey.Of("Interlude", "A", "Album", 3);
        Assert.NotEqual(song, TrackKey.Of("Interlude", "A", "Album", 7)); // the same name twice on one album
        Assert.NotEqual(song, TrackKey.Of("Interlude", "A", "Album (Deluxe)", 3));
        Assert.NotEqual(song, TrackKey.Of("Interlude", "B", "Album", 3));
        Assert.NotEqual(song, TrackKey.Of("interlude", "A", "Album", 3));
    }

    [Fact]
    public void FieldsDoNotRunIntoEachOther()
    {
        Assert.NotEqual(TrackKey.Of("Song", "A B", "C", 1), TrackKey.Of("Song A", "B", "C", 1));
    }

    [Fact]
    public void NoTitleMeansNothingIsLoaded()
    {
        Assert.Null(TrackKey.Of("", "", "", 0));
        Assert.Null(TrackKey.Of(null, "Artist", "Album", 1));
    }

    [Fact]
    public void TheIdNamesNothing()
    {
        var id = TrackKey.Of("KARANLIK", "Yung Kafa & Kücük Efendi", "KARANLIK", 1)!;
        Assert.StartsWith(TrackKey.Prefix, id);
        Assert.Matches("^smtc:[0-9a-f]{64}$", id);
        Assert.DoesNotContain("KARANLIK", id);
    }
}
