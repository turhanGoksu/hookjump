using System.Linq;
using Hookjump.Core;
using Hookjump.Player;
using Xunit;

namespace Hookjump.Tests;

/// <summary>The same sentences as tests/test_words.py on the Mac and WordsTest.kt on the phone.</summary>
public class WordsTest
{
    [Fact]
    public void Clock()
    {
        Assert.Equal(["0:00", "1:43", "0:59", "10:00"], new long[] { 0, 103500, 59999, 600000 }.Select(Words.Clock));
    }

    [Fact]
    public void ALandedJump()
    {
        var jumped = new Jumped(new JumpResult(Status.Personal, 103500), Landing.Landed);
        Assert.Equal("Jumped to 1:43: the part you replay most.", Words.DescribeJump(jumped));
    }

    [Fact]
    public void ASlippedJumpSaysWhatHappened()
    {
        var jumped = new Jumped(new JumpResult(Status.Pinned, 72000), Landing.Slipped);
        Assert.Equal("Tried 1:12 (your pinned spot), but Spotify did not move there.", Words.DescribeJump(jumped));
    }

    [Fact]
    public void NoJumpSaysWhy()
    {
        var noData = Words.DescribeJump(new Jumped(new JumpResult(Status.NoData), null));
        Assert.StartsWith("No jump: ", noData);
        Assert.Contains("nothing pinned or learned", noData);
        var closed = Words.DescribeJump(new Jumped(new JumpResult(Status.PlayerUnavailable), null));
        Assert.StartsWith("No jump: ", closed);
        Assert.Contains("Spotify is not playing", closed);
    }

    [Fact]
    public void WhatAJumpWouldDoIsSaidBeforeIt()
    {
        Assert.Equal("Jumps to 1:46: your pinned spot.", Words.DescribeTarget(new JumpResult(Status.Pinned, 106200)));
        Assert.Equal(
            "No jump: nothing pinned or learned for this song yet.", Words.DescribeTarget(new JumpResult(Status.NoData)));
        Assert.Equal("Spotify is not playing anything.", Words.DescribeTarget(new JumpResult(Status.PlayerUnavailable)));
    }

    [Fact]
    public void Pins()
    {
        Assert.Equal("Pinned 1:12. The jump lands 1.5 s before it.", Words.DescribePin(72000));
        Assert.Equal("Pinned 1:23. The jump lands right there.", Words.DescribePin(83000, exact: true));
        Assert.StartsWith("Nothing pinned", Words.DescribePin(null));
    }

    [Fact]
    public void RemovingAPinSaysWhatHappened()
    {
        Assert.Equal("Pin removed.", Words.DescribeUnpin(true));
        Assert.Equal("Nothing to remove: this song has no pin.", Words.DescribeUnpin(false));
    }

    [Fact]
    public void ChangesSayWhatWasDone()
    {
        Assert.Equal("Forgot this song: its pin and what was learned.", Words.DescribeForget(true));
        Assert.Equal("Forgot what was learned from this song. Its pin stays.", Words.DescribeForgetLearned(true));
        Assert.Equal(Words.NoSong, Words.DescribeForgetLearned(false));
        Assert.Equal("Forgot what was learned from every song. Pins stay.", Words.ForgotAllLearned);
        Assert.Equal("Not tracking this song. Its pin stays.", Words.DescribeIgnore(true));
        Assert.Equal("Learning paused. Pins still work.", Words.DescribePause(true));
        Assert.Equal(Words.NoSong, Words.DescribeTrackAgain(false));
    }

    [Fact]
    public void ATypedTimeMustBeATimeWithinTheSong()
    {
        Assert.Equal("Type a time like 1:23.", Words.TypedTimeProblem(Words.ParseClock("abc"), 192000));
        Assert.Equal("This song is 3:12 long.", Words.TypedTimeProblem(Words.ParseClock("3:30"), 192000));
        Assert.Null(Words.TypedTimeProblem(Words.ParseClock("1:23"), 192000));
        Assert.Null(Words.TypedTimeProblem(Words.ParseClock("3:12"), 192000));
    }

    [Fact]
    public void ARecordSaysWhatIsKeptOneFactPerLine()
    {
        var pinned = new Record("Ou", "UZI", new Pin(83000, true), new Favorite(107900, 9, 2), 4, 2, Ignored: false);
        Assert.Equal(["Pinned at 1:23, typed", "Learned 1:47, from 2 listens"], Words.DescribeRecord(pinned));
        var learning = new Record("Ou", "UZI", null, null, 1, 1, Ignored: false);
        Assert.Equal(["1 seek in 1 listen, nothing learned yet"], Words.DescribeRecord(learning));
        var ignored = new Record(null, null, new Pin(1000, false), null, 0, 0, Ignored: true);
        Assert.Equal(["Pinned at 0:01", "Not tracking"], Words.DescribeRecord(ignored));
        Assert.Equal("A song not played since names were kept", Words.RecordTitle(ignored));
    }

    [Fact]
    public void ARecordIsNamedByTitleAndArtist()
    {
        // The same cases as tests/test_words.py.
        Assert.Equal("Ou — UZI, Motive", Words.RecordName(new Record("Ou", "UZI, Motive", null, null, 0, 0, false)));
        Assert.Equal("Ou", Words.RecordName(new Record("Ou", null, null, null, 0, 0, false)));
        Assert.Null(Words.RecordName(new Record(null, null, null, null, 0, 0, false)));
    }

    [Fact]
    public void SongsWithoutAName()
    {
        Assert.Equal("1 song not played since names were kept", Words.UnnamedSongs(1));
        Assert.Equal("32 songs not played since names were kept", Words.UnnamedSongs(32));
    }

    [Fact]
    public void Shorten()
    {
        Assert.Equal("Ou — UZI, Motive", Words.Shorten("Ou — UZI, Motive"));
        const string name = "KARANLIK — Yung Kafa & Kücük Efendi, Motive, Jumpa, Yung Kafa";
        var shortened = Words.Shorten(name);
        Assert.Equal(48, shortened.Length);
        Assert.EndsWith("…", shortened);
        Assert.StartsWith(shortened[..^1], name);
        Assert.Equal("abc d", Words.Shorten("abc d", 5));
        Assert.Equal("abc…", Words.Shorten("abc def", 5));
    }

    [Theory]
    [InlineData("1:23", 83000L)]
    [InlineData(" 1:23 ", 83000L)]
    [InlineData("1:23.5", 83500L)]
    [InlineData("0:07", 7000L)]
    [InlineData("83", 83000L)]
    [InlineData("12:00", 720000L)]
    [InlineData("0:00.0005", 1L)] // half a millisecond rounds up, as on Android
    public void ParseClock(string text, long ms)
    {
        Assert.Equal(ms, Words.ParseClock(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:60")]
    [InlineData("1:-5")]
    [InlineData("-1:20")]
    [InlineData("1:2:3")]
    [InlineData("nan")]
    [InlineData("Infinity")]
    public void ParseClockRefuses(string text)
    {
        Assert.Null(Words.ParseClock(text));
    }
}
