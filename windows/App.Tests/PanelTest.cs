using System.Linq;
using Xunit;

namespace Hookjump.App.Tests;

/// <summary>The sea panel's grid as data: what a click lands on, where Tab goes, which times fit.</summary>
public class PanelTest
{
    private const long Sorma = 279267; // 4:39

    [Fact]
    public void TheLineRunsFromTheStartToTheEndOfTheSong()
    {
        Assert.Equal(Panel.LineLeft, Panel.X(0, Sorma));
        Assert.Equal(Panel.LineRight, Panel.X(Sorma, Sorma));
        Assert.Equal(Panel.LineRight, Panel.X(Sorma + 5000, Sorma)); // never past its end
        Assert.Equal(0, Panel.MsAt(Panel.LineLeft - 4, Sorma));
        Assert.Equal(279_000, Panel.MsAt(Panel.LineRight + 4, Sorma)); // the last whole second
    }

    [Fact]
    public void AClickOnTheLinePinsTheWholeSecondUnderIt()
    {
        var x = Panel.X(103_400, Sorma);
        Assert.Equal(PanelPart.Line, Panel.Hit(x, Panel.LineY + Panel.Reach, hasChart: true, pinned: false));
        Assert.Equal(103_000, Panel.MsAt(x, Sorma));
        Assert.Equal(PanelPart.None, Panel.Hit(x, Panel.LineY + Panel.Reach + 1, hasChart: true, pinned: false));
        // The very start and the very end take a click too; the line down from the surface does not.
        Assert.Equal(PanelPart.Line, Panel.Hit(Panel.LineLeft - 5, Panel.LineY, true, false));
        Assert.Equal(PanelPart.Line, Panel.Hit(Panel.LineRight + 5, Panel.LineY, true, false));
        Assert.Equal(PanelPart.None, Panel.Hit(Panel.LineLeft - 8, Panel.LineY, true, false));
        Assert.Equal(PanelPart.None, Panel.Hit(Panel.LineRight + 8, Panel.LineY, true, false));
        Assert.Equal(PanelPart.None, Panel.Hit(Panel.LineLeft, Panel.SeaTop + 10, true, false));
    }

    [Fact]
    public void TheButtonsAndTheRemoveLinkTakeTheirClicks()
    {
        Assert.Equal(PanelPart.Jump, Panel.Hit(Panel.Jump.X + 1, Panel.Jump.Y + 1, true, false));
        Assert.Equal(PanelPart.Pin, Panel.Hit(Panel.Pin.Right - 1, Panel.Pin.Bottom - 1, true, false));
        Assert.Equal(PanelPart.None, Panel.Hit((Panel.Jump.Right + Panel.Pin.X) / 2, Panel.Jump.Y + 10, true, false));
        var link = (Panel.RemovePin.X + 4, Panel.RemovePin.Y + 4);
        Assert.Equal(PanelPart.RemovePin, Panel.Hit(link.Item1, link.Item2, true, pinned: true));
        Assert.Equal(PanelPart.None, Panel.Hit(link.Item1, link.Item2, true, pinned: false)); // no pin, no link
    }

    [Fact]
    public void TheSongButtonsAreRoundAndInTheMiddle()
    {
        static (float X, float Y) Middle(Box b) => (b.X + b.Width / 2, b.Y + b.Height / 2);
        Assert.Equal(Panel.Width / 2, Middle(Panel.PlayPause).X);
        Assert.Equal(Middle(Panel.PlayPause).X - Middle(Panel.Previous).X, Middle(Panel.Next).X - Middle(Panel.PlayPause).X);
        foreach (var (box, part) in new[]
        {
            (Panel.Previous, PanelPart.Previous), (Panel.PlayPause, PanelPart.PlayPause), (Panel.Next, PanelPart.Next),
        })
        {
            var (x, y) = Middle(box);
            Assert.Equal(part, Panel.Hit(x, y, true, false));
            Assert.Equal(part, Panel.Hit(x + box.Width / 2 - 1, y, true, false)); // its edge
            Assert.Equal(PanelPart.None, Panel.Hit(box.X + 2, box.Y + 2, true, false)); // a corner is outside the circle
        }
        // Above Jump and Pin, apart from them and from the words.
        Assert.True(Panel.PlayPause.Bottom < Panel.Jump.Y);
        Assert.True(Panel.PlayPause.Y >= Panel.RemovePin.Bottom);
        Assert.True(Panel.Jump.Bottom + Panel.Pad / 2 <= Panel.FullHeight);
    }

    [Fact]
    public void WithNothingPlayingNothingTakesAClick()
    {
        Assert.Equal(PanelPart.None, Panel.Hit(Panel.Jump.X + 1, Panel.Jump.Y + 1, hasChart: false, pinned: false));
        Assert.Equal(PanelPart.None, Panel.NextFocus(PanelPart.None, false, hasChart: false, pinned: false));
        Assert.True(Panel.HeightFor(hasChart: false) < Panel.HeightFor(hasChart: true));
    }

    [Fact]
    public void TabGoesRoundWhatCanBePressed()
    {
        // In reading order: the song buttons, then Jump and Pin, and round again.
        var order = new[] { PanelPart.Previous, PanelPart.PlayPause, PanelPart.Next, PanelPart.Jump, PanelPart.Pin };
        var at = PanelPart.None;
        foreach (var expected in order.Concat(order))
        {
            at = Panel.NextFocus(at, false, true, pinned: false);
            Assert.Equal(expected, at);
        }
        Assert.Equal(PanelPart.Pin, Panel.NextFocus(PanelPart.None, backwards: true, true, false));
        Assert.Equal(PanelPart.Next, Panel.NextFocus(PanelPart.Jump, backwards: true, true, false));
        Assert.Equal(PanelPart.RemovePin, Panel.NextFocus(PanelPart.None, false, true, pinned: true));
        Assert.Equal(PanelPart.RemovePin, Panel.NextFocus(PanelPart.Previous, backwards: true, true, pinned: true));
        Assert.Equal(PanelPart.Previous, Panel.NextFocus(PanelPart.RemovePin, false, true, pinned: false)); // the pin was removed
    }

    [Fact]
    public void UnderTheLineTheStartTheEndAndEachMinuteAreWritten()
    {
        var marks = Panel.Times(Sorma, floatMs: null, pointerMs: null);
        Assert.Equal(
            [(0L, TimeKind.Start), (Sorma, TimeKind.End), (60_000L, TimeKind.Minute), (120_000L, TimeKind.Minute),
                (180_000L, TimeKind.Minute), (240_000L, TimeKind.Minute)],
            marks.Select(m => (m.Ms, m.Kind)));
        Assert.Equal(Panel.LineLeft, marks[0].Left);
        Assert.Equal(Panel.LineRight, marks[1].Right, 3);
    }

    [Fact]
    public void ThePlayheadsTimeComesFirstAndTheOthersMakeWay()
    {
        var marks = Panel.Times(Sorma, floatMs: 61_000, pointerMs: null);
        Assert.Equal((61_000L, TimeKind.Float), (marks[0].Ms, marks[0].Kind));
        // It is written larger, so it takes more room: four characters of Plex Mono, each 0.6 of the size.
        Assert.Equal(4 * 0.6 * Panel.LeadSize, marks[0].Right - marks[0].Left, 3);
        Assert.Equal(4 * 0.6 * Panel.MarkSize, marks[1].Right - marks[1].Left, 3);
        Assert.DoesNotContain(60_000L, marks.Select(m => m.Ms)); // 1:00 would sit under 1:01
        Assert.Contains(120_000L, marks.Select(m => m.Ms));
        Assert.All(marks.Zip(marks.Skip(1)), pair => Assert.False(Overlap(pair.First, pair.Second)));
    }

    [Fact]
    public void ThePointersTimeComesBeforeThePlayheads()
    {
        var marks = Panel.Times(Sorma, floatMs: 100_000, pointerMs: 101_000);
        Assert.Equal((101_000L, TimeKind.Pointer), (marks[0].Ms, marks[0].Kind));
        Assert.DoesNotContain(TimeKind.Float, marks.Select(m => m.Kind)); // too close: the pointer's says it
        var apart = Panel.Times(Sorma, floatMs: 30_000, pointerMs: 200_000);
        Assert.Equal([TimeKind.Pointer, TimeKind.Float], apart.Take(2).Select(m => m.Kind));
    }

    [Fact]
    public void ATimeNearTheEdgeStaysInsideThePanel()
    {
        var start = Panel.Times(Sorma, floatMs: 0, pointerMs: null)[0];
        Assert.True(start.Left >= Panel.Pad / 2);
        var end = Panel.Times(Sorma, floatMs: Sorma, pointerMs: null)[0];
        Assert.True(end.Right <= Panel.Width - Panel.Pad / 2);
    }

    [Fact]
    public void TheClickThatClosedThePanelDoesNotOpenItAgain()
    {
        Assert.False(Panel.OpensOnClick(nowMs: 10_200, closedAtMs: 10_000));
        Assert.True(Panel.OpensOnClick(nowMs: 10_400, closedAtMs: 10_000));
    }

    private static bool Overlap(TimeMark a, TimeMark b) => a.Left < b.Right && b.Left < a.Right;
}
