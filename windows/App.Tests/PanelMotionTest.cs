using System;
using Xunit;

namespace Hookjump.App.Tests;

/// <summary>The panel's motions as data: when a pin is cast or reeled, and where it is at each moment.</summary>
public class PanelMotionTest
{
    private static readonly HookWords Learned = new("Sorma", 103_500, false, "The part you replay most", "learned from 3 listens");
    private static readonly HookWords Pinned = new("Sorma", 72_000, true, "Your pinned spot", "the jump lands 1.5 s before it");

    [Fact]
    public void ANewPinIsCastAndARemovedOneReeled()
    {
        Assert.Equal(new HookChange(true, Learned), HookChange.Of(Learned, Pinned, motion: true));
        Assert.Equal(new HookChange(true, Pinned), HookChange.Of(Pinned, Pinned with { Ms = 80_000 }, true)); // moved
        Assert.Equal(new HookChange(false, Pinned), HookChange.Of(Pinned, Learned, true));
        Assert.Equal(new HookChange(false, Pinned), HookChange.Of(Pinned, Learned with { Ms = null }, true));
    }

    [Fact]
    public void NothingMovesWithoutACauseOrWithoutMotion()
    {
        Assert.Null(HookChange.Of(Pinned, Pinned, true)); // the same pin, heard again
        Assert.Null(HookChange.Of(Learned, Learned with { Ms = 90_000 }, true)); // learning moved: no cast
        Assert.Null(HookChange.Of(Pinned, Pinned with { Title = "Ou", Ms = 10_000 }, true)); // another song
        Assert.Null(HookChange.Of(null, Pinned, true)); // the panel just opened
        Assert.Null(HookChange.Of(Learned, Pinned, motion: false)); // Windows' animations are off
    }

    [Fact]
    public void ACastFliesInSplashesAndSinksToItsSecond()
    {
        var cast = new HookChange(true, Learned);
        var flying = cast.At(0.2);
        Assert.InRange(flying.Flight!.Value, 0.5, 0.6);
        Assert.Null(flying.Depth); // in the air, not yet in the water
        var landing = cast.At(0.4);
        Assert.Null(landing.Flight);
        Assert.NotNull(landing.Splash);
        Assert.InRange(landing.Depth!.Value, 0.01, 0.99);
        // It sinks fast, then settles: a sixth of the way in time is well past a third in depth.
        Assert.True(cast.At(0.5).Depth > 0.33);
        var settled = cast.At(HookChange.CastSeconds);
        Assert.Equal(1, settled.Depth!.Value, 6);
        Assert.Null(settled.Splash);
    }

    [Fact]
    public void AReelRisesToTheSurfaceAndFliesOff()
    {
        var reel = new HookChange(false, Pinned);
        Assert.Equal(1, reel.At(0).Depth!.Value, 6); // starts in its place
        Assert.InRange(reel.At(0.4).Depth!.Value, 0.7, 0.99); // slow to start, faster near the top
        var leaving = reel.At(0.75);
        Assert.Null(leaving.Depth);
        Assert.InRange(leaving.Flight!.Value, 0.5, 0.99); // going back up to the right
        Assert.NotNull(leaving.Splash);
        Assert.Equal(0, reel.At(HookChange.ReelSeconds).Flight!.Value, 6); // gone
    }

    [Fact]
    public void TheOldWordsGoAtTheStartAndTheNewComeAtTheEnd()
    {
        var cast = new HookChange(true, Learned);
        Assert.Equal((0.0, 1.0), (cast.At(0).Words, cast.At(0).OldWords));
        var middle = cast.At(HookChange.CastSeconds / 2);
        Assert.Equal((0.0, 0.0), (middle.Words, middle.OldWords));
        Assert.Equal(0, cast.At(HookChange.CastSeconds * 0.8).Words); // not while the hook still sinks
        Assert.Equal((1.0, 0.0), (cast.At(HookChange.CastSeconds).Words, cast.At(HookChange.CastSeconds).OldWords));
    }

    [Fact]
    public void TheSeaMovesOnlyWhileTheSongPlaysWithMotion()
    {
        var sea = new SeaClock();
        sea.Advance(1_000, playing: false, motion: true);
        sea.Advance(1_000, playing: true, motion: false);
        Assert.Equal(0, sea.Ms);
        sea.Advance(1_300, playing: true, motion: true);
        Assert.Equal(0.5, sea.Cycle(2_600), 6);
        sea.Advance(2_600, true, true);
        Assert.Equal(0.5, sea.Cycle(2_600), 6); // round and round
    }

    [Fact]
    public void TheCurrentBendsTheLineOnlyBetweenItsEnds()
    {
        var sea = new SeaClock();
        sea.Advance(11_000 / 4, true, true); // the current at its strongest
        Assert.Equal(0, Sway.Line(sea, Panel.LineLeft), 4);
        Assert.Equal(0, Sway.Line(sea, Panel.LineRight), 4);
        Assert.Equal(5, Sway.Line(sea, (Panel.LineLeft + Panel.LineRight) / 2), 4);
    }

    [Fact]
    public void TheFloatBobsAndTiltsALittle()
    {
        var sea = new SeaClock();
        Assert.Equal(0, Sway.Bob(sea), 4);
        sea.Advance(2_600 / 4, true, true); // a quarter of a bob
        Assert.Equal(1.6, Sway.Bob(sea), 4); // pixels
        Assert.Equal(4 * Math.Sin(Math.PI / 2 + 0.6), Sway.Tilt(sea), 4); // degrees, a little after the bob
    }

    [Fact]
    public void AfterASeekTheFloatGlidesInAndSettles()
    {
        Assert.Equal(-120, Glide.Offset(-120, 0));
        var halfway = Glide.Offset(-120, 0.15);
        Assert.InRange(halfway, -100, -10); // on its way
        Assert.True(Math.Abs(Glide.Offset(-120, 1.0)) < 1); // there within a second
        Assert.False(Glide.Settled(-120, 0.3));
        Assert.True(Glide.Settled(-120, 1.2));
        Assert.True(Glide.Settled(0.1, 0)); // too small to see, even with the spring's overshoot
    }

    [Theory]
    [InlineData(-120)]
    [InlineData(300)]
    [InlineData(-406)] // the whole line
    public void OnceSettledTheFloatNeverMovesVisiblyAgain(double from)
    {
        // The spring overshoots: settling when the distance alone looks small
        // would leave the float still swinging, then snap it into place.
        var settled = 0.0;
        while (!Glide.Settled(from, settled)) settled += 0.01;
        for (var t = settled; t < settled + 3; t += 0.01) Assert.True(Math.Abs(Glide.Offset(from, t)) < 0.3, $"at {t:0.00} s");
    }
}
