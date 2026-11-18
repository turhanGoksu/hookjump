using Hookjump.Core;
using Hookjump.Player;
using Xunit;

namespace Hookjump.Tests;

/// <summary>The chart, as data: the same cases as tests/test_sleeve.py on the Mac and ChartTest.kt on the phone.</summary>
public class ChartTest
{
    private const long T0 = 1_700_000_000_000;
    private static readonly Reading Playing = new(T0, "a", "Alışamadım", "Ezhel", 153268, true, 40000);
    private static readonly SeekEvent[] Seeks = [new("a", 1, 130000, 105000), new("a", 2, 10000, 60000)];

    [Fact]
    public void APinIsTheHook()
    {
        var chart = Chart.For(Playing, new Pin(72000, Typed: false), null, Seeks)!;
        Assert.Equal(("Alışamadım", "Ezhel", (long?)72000), (chart.Title, chart.Artist, chart.HookMs));
        Assert.Equal(("Your pinned spot", "the jump lands 1.5 s before it"), (chart.HookLabel, chart.HookDetail));
        Assert.Equal([105000L], chart.Rewinds);
        Assert.Equal([60000L], chart.Skips);
        Assert.True(chart.Pinned);
        Assert.False(Chart.For(Playing, null, new Favorite(105000, 6, 2), Seeks)!.Pinned);
    }

    [Fact]
    public void ATypedPinSaysSo()
    {
        Assert.Equal("the jump lands right there", Chart.For(Playing, new Pin(83000, Typed: true), null, [])!.HookDetail);
    }

    [Fact]
    public void ALearnedFavoriteSaysHowItWasLearned()
    {
        var chart = Chart.For(Playing, null, new Favorite(105000, 6, 2), Seeks)!;
        Assert.Equal(((long?)105000, "The part you replay most", "learned from 2 listens"), (chart.HookMs, chart.HookLabel, chart.HookDetail));
    }

    [Fact]
    public void NothingKnownSaysWhatToDo()
    {
        var chart = Chart.For(Playing, null, null, [])!;
        Assert.Null(chart.HookMs);
        Assert.Equal("Nothing learned yet", chart.HookLabel);
    }

    [Fact]
    public void WithNoHookItSaysWhyNothingIsLearned()
    {
        var ignored = Chart.For(Playing, null, null, [], ignored: true)!;
        Assert.Equal(("Not learning from this song", "a pin still works"), (ignored.HookLabel, ignored.HookDetail));
        var paused = Chart.For(Playing, null, null, [], learningPaused: true)!;
        Assert.Equal(("Learning is paused", "a pin still works"), (paused.HookLabel, paused.HookDetail));
        // A pin is still the hook, whatever is learned or not.
        Assert.Equal(
            "Your pinned spot",
            Chart.For(Playing, new Pin(1000, false), null, [], ignored: true, learningPaused: true)!.HookLabel);
    }

    [Fact]
    public void NoChartWithoutATrackToDraw()
    {
        Assert.Null(Chart.For(null, null, null, []));
        Assert.Null(Chart.For(Playing with { TrackId = null }, null, null, []));
        Assert.Null(Chart.For(Playing with { DurationMs = null }, null, null, [])); // no length
    }

    [Fact]
    public void AMissingTitleOrArtist()
    {
        var noTitle = Chart.For(Playing with { Title = null }, null, null, [])!;
        Assert.Equal(("Ezhel", (string?)null), (noTitle.Title, noTitle.Artist));
        Assert.Equal("Unknown track", Chart.For(Playing with { Title = null, Artist = null }, null, null, [])!.Title);
    }

    [Fact]
    public void TheFloatMovesOnWhilePlayingAndStopsAtTheEnd()
    {
        var chart = Chart.For(Playing, null, null, [])!;
        Assert.Equal(45000L, chart.NowAt(T0 + 5000));
        Assert.Equal(153268L, chart.NowAt(T0 + 600000));
        Assert.Equal(40000L, Chart.For(Playing with { Playing = false }, null, null, [])!.NowAt(T0 + 5000));
    }

    [Fact]
    public void FractionAndBack()
    {
        Assert.Equal(0.5, Chart.Fraction(76634, 153268));
        Assert.Equal((0.0, 1.0), (Chart.Fraction(-5, 153268), Chart.Fraction(999999, 153268)));
        Assert.Equal(77000L, Chart.MsAt(0.5, 153268)); // whole seconds
        Assert.Equal(153000L, Chart.MsAt(1.2, 153268)); // the end, to the whole second
        Assert.Equal(153600L, Chart.MsAt(1.0, 153600)); // rounding up never passes the end
        Assert.Equal(0L, Chart.MsAt(-0.1, 153268));
        Assert.Equal(3000L, Chart.MsAt(0.5, 5000)); // 2.5 s rounds up, as on Android, not to even
    }
}
