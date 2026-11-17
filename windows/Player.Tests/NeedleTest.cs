using System.Collections.Generic;
using Xunit;

namespace Hookjump.Player.Tests;

public class NeedleTest
{
    private readonly FakeTurntable _player = new();
    private readonly Ear _ear = new();
    private readonly Needle _needle;
    private Landing? _landing;

    public NeedleTest() => _needle = new Needle(_player, _ear);

    private void Drop(long targetMs = 100000) => _needle.Drop("a", targetMs, _player.AtMs, l => _landing = l);

    [Fact]
    public void AJumpLandsWhenThePlayerReportsTheNewPlace()
    {
        Drop();
        Assert.Null(_landing); // waits for the player
        _needle.Heard(_player.Report(), _player.AtMs);
        Assert.Equal(Landing.Landed, _landing);
        Assert.Equal(1, _player.Places);
    }

    [Fact]
    public void AReportFromBeforeTheMoveIsNotAnAnswer()
    {
        _player.AtMs += 5000;
        Drop();
        _needle.Heard(_player.Read()! with { AtMs = _player.AtMs - 1000, PositionMs = 30000 }, _player.AtMs);
        Assert.Null(_landing);
        Assert.Equal(1, _player.Places); // no second try because of an old report
    }

    [Fact]
    public void ASilentPlayerIsTriedOnceMoreThenTheJumpHasSlipped()
    {
        Drop();
        _needle.Tick(_player.AtMs + Needle.SettleMs);
        Assert.Equal(2, _player.Places);
        Assert.Null(_landing);
        _needle.Tick(_player.AtMs + 2 * Needle.SettleMs);
        Assert.Equal(Landing.Slipped, _landing);
        Assert.Equal(2, _player.Places);
    }

    [Fact]
    public void APlayerThatIgnoresTheFirstMoveLandsOnTheSecond()
    {
        _player.Ignores = 1;
        Drop();
        _needle.Heard(_player.Report(), _player.AtMs); // still at 0:30
        Assert.Equal(2, _player.Places);
        _needle.Heard(_player.Report(), _player.AtMs);
        Assert.Equal(Landing.Landed, _landing);
    }

    [Fact]
    public void APlayerThatIgnoresBothMovesIsReported()
    {
        _player.Ignores = 2;
        Drop();
        _needle.Heard(_player.Report(), _player.AtMs);
        _needle.Heard(_player.Report(), _player.AtMs);
        Assert.Equal(Landing.Slipped, _landing);
    }

    [Fact]
    public void LandingCloseEnoughCounts()
    {
        Drop(100000);
        _needle.Heard(_player.Report()! with { PositionMs = 101500 }, _player.AtMs);
        Assert.Equal(Landing.Landed, _landing);
    }

    [Fact]
    public void ANewTrackIsNeverMoved()
    {
        _player.SwitchTo = "b";
        Drop();
        Assert.Equal(Landing.TrackChanged, _landing);
        Assert.Equal("b", _player.Track);
        Assert.Equal(0L, _player.PositionMs);
    }

    [Fact]
    public void ATrackChangeWhileWaitingIsReported()
    {
        Drop();
        _needle.Heard(_player.Report()! with { TrackId = "b" }, _player.AtMs);
        Assert.Equal(Landing.TrackChanged, _landing);
    }

    [Fact]
    public void AClosedPlayerIsReported()
    {
        _player.Running = false;
        Drop();
        Assert.Equal(Landing.NoPlayer, _landing);
    }

    [Fact]
    public void ANewPressReplacesAJumpStillWaitingAndTheOldOneIsReported()
    {
        // Nothing is left unanswered: the first jump says it slipped.
        var first = new List<Landing>();
        _needle.Drop("a", 100000, _player.AtMs, first.Add);
        Drop(60000);
        Assert.Equal([Landing.Slipped], first);
        _needle.Heard(_player.Report(), _player.AtMs);
        Assert.Equal(Landing.Landed, _landing);
        Assert.Equal(60000L, _player.PositionMs);
    }

    [Fact]
    public void TheJumpMarksItsOwnMoveForTheEar()
    {
        _ear.Hear(_player.Read(), _player.AtMs);
        Drop();
        var samples = _ear.Hear(_player.Report(), _player.AtMs);
        Assert.True(samples[^1].OwnSeek);
    }
}
