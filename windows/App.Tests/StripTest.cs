using System;
using Hookjump.Core;
using Hookjump.Player;
using Xunit;

namespace Hookjump.App.Tests;

/// <summary>The strip's timing, as data: where things are at each moment.</summary>
public class StripTest
{
    private static readonly StripScene Jump = new("Sorma", 279267, 140000, 103500, Moves: true, HookDrops: false, "Jumped to 1:43: the part you replay most.");
    private static readonly StripScene Pin = new("Sorma", 279267, 72000, 72000, Moves: false, HookDrops: true, "Pinned 1:12. The jump lands 1.5 s before it.");
    private static readonly StripScene NoJump = new("Sorma", 279267, 140000, null, Moves: false, HookDrops: false, "No jump: nothing pinned or learned for this song yet.");

    [Fact]
    public void ItComesInStaysToBeReadAndGoes()
    {
        Assert.Equal(0, Strip.At(Jump, 0).Alpha);
        Assert.Equal(1, Strip.At(Jump, 1.0).Alpha);
        Assert.InRange(Strip.At(Jump, Strip.Seconds - 0.2).Alpha, 0.4, 0.6);
        Assert.True(Strip.At(Jump, Strip.Seconds).Done);
        Assert.False(Strip.At(Jump, Strip.Seconds - 0.01).Done);
    }

    [Fact]
    public void AJumpSlidesTheFloatToTheHook()
    {
        Assert.Equal(140000, Strip.At(Jump, 0.05).FloatMs);
        var halfway = Strip.At(Jump, 0.3).FloatMs!.Value;
        Assert.InRange(halfway, 103500, 140000 - 1);
        // It starts fast and settles onto the hook: a quarter of the time covers more than half the way.
        var quarter = Strip.At(Jump, 0.1 + 0.7 / 4).FloatMs!.Value;
        Assert.InRange((140000 - quarter) / (140000 - 103500), 0.5, 0.7);
        Assert.Equal(103500, Strip.At(Jump, 1.0).FloatMs!.Value, 3);
        Assert.Equal(103500, Strip.At(Jump, 0.3).HookMs);
        Assert.Equal(1, Strip.At(Jump, 0.3).HookDepth); // the hook was already there
    }

    [Fact]
    public void APinDropsTheHookWhereTheFloatIs()
    {
        Assert.Equal(0, Strip.At(Pin, 0.05).HookDepth);
        Assert.InRange(Strip.At(Pin, 0.3).HookDepth, 0.01, 0.99);
        Assert.Equal(1, Strip.At(Pin, 1.0).HookDepth, 6);
        Assert.Equal(72000, Strip.At(Pin, 0.3).FloatMs);
        Assert.Equal(0, Strip.At(Pin, 0.2).Wiggle); // a pin is no refusal: the float stays still
    }

    [Fact]
    public void NowhereToGoShakesTheFloatAndShowsNoHook()
    {
        Assert.Null(Strip.At(NoJump, 0.3).HookMs);
        Assert.Equal(140000, Strip.At(NoJump, 0.3).FloatMs);
        Assert.Equal(0, Strip.At(NoJump, 0.05).Wiggle); // first the strip comes in
        Assert.NotEqual(0, Strip.At(NoJump, 0.2).Wiggle);
        // Each swing is smaller than the one before, then it stops.
        var first = Math.Abs(Strip.At(NoJump, 0.1 + 0.7 / 8).Wiggle);
        var third = Math.Abs(Strip.At(NoJump, 0.1 + 0.7 * 5 / 8).Wiggle);
        Assert.InRange(third, 0.01, first / 2);
        Assert.Equal(0, Strip.At(NoJump, 1.0).Wiggle);
        Assert.Equal(0, Strip.At(Jump, 0.2).Wiggle); // a jump that lands does not shake
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)] // a line of no length cannot place anything on it
    public void ASongOfUnknownLengthHasNoLineOnlyWords(long? duration)
    {
        var frame = Strip.At(Jump with { DurationMs = duration }, 0.5);
        Assert.Null(frame.FloatMs);
        Assert.Null(frame.HookMs);
        Assert.Equal(1, frame.Alpha);
    }

    // Which scene a key shows -------------------------------------------------

    private static readonly Reading Sorma = new(0, "smtc:1", "Sorma", "Pinhani", 279267, true, 140000);

    [Fact]
    public void AJumpThatLandedSlidesFromWherePlaybackWas()
    {
        var scene = Strip.ForJump(Sorma, 140000, new Jumped(new JumpResult(Status.Personal, 103500), Landing.Landed));
        Assert.Equal(new StripScene("Sorma", 279267, 140000, 103500, Moves: true, HookDrops: false,
            "Jumped to 1:43: the part you replay most."), scene);
    }

    [Theory]
    [InlineData(Landing.Slipped)]
    [InlineData(Landing.TrackChanged)]
    [InlineData(Landing.NoPlayer)]
    public void AJumpThatDidNotLandShowsTheHookButKeepsTheFloat(Landing landing)
    {
        var scene = Strip.ForJump(Sorma, 140000, new Jumped(new JumpResult(Status.Pinned, 72000), landing));
        Assert.False(scene.Moves);
        Assert.Equal(140000, scene.FromMs);
        Assert.Equal(72000, scene.ToMs);
        Assert.StartsWith("Tried 1:12", scene.Words);
        Assert.NotEqual(0, Strip.At(scene, 0.2).Wiggle); // it shakes: nothing moved
    }

    [Fact]
    public void AJumpWithNowhereToGoHasNoHook()
    {
        var scene = Strip.ForJump(Sorma, 140000, new Jumped(new JumpResult(Status.NoData), null));
        Assert.False(scene.Moves);
        Assert.Null(scene.ToMs);
        Assert.Equal("No jump: nothing pinned or learned for this song yet.", scene.Words);
    }

    [Fact]
    public void AJumpWithSpotifyClosedNamesNoSong()
    {
        var scene = Strip.ForJump(null, null, new Jumped(new JumpResult(Status.PlayerUnavailable), null));
        Assert.Equal("hookjump", scene.Title);
        Assert.False(scene.HasLine);
        Assert.StartsWith("No jump:", scene.Words);
    }

    [Theory]
    [InlineData(false, "Pinned 1:12. The jump lands 1.5 s before it.")]
    [InlineData(true, "Pinned 1:12. The jump lands right there.")]
    public void APinDropsTheHookOnTheSong(bool typed, string words)
    {
        var scene = Strip.ForPin(Sorma, new Pin(72000, typed));
        Assert.Equal(new StripScene("Sorma", 279267, 72000, 72000, Moves: false, HookDrops: true, words), scene);
    }

    [Fact]
    public void NoPinWithNothingPlayingShowsOnlyWords()
    {
        // The reading may still hold a song that has just ended; with no pin it is not shown.
        var scene = Strip.ForPin(Sorma, null);
        Assert.Equal("hookjump", scene.Title);
        Assert.False(scene.HasLine);
        Assert.False(scene.HookDrops);
        Assert.Equal("Nothing pinned: Spotify is not playing anything.", scene.Words);
    }
}
