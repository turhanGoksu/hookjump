using System;
using System.Linq;
using Xunit;

namespace Hookjump.Core.Tests;

/// <summary>A jump result has a target exactly when its status says it should.</summary>
public class ModelsTest
{
    private static readonly Status[] All = Enum.GetValues<Status>();

    [Fact]
    public void StatusValuesMatchTheSpec()
    {
        // spec/ALGORITHM.md section 2 and every JSON vector use these strings.
        Assert.Equal(
            ["no_data", "personal", "pinned", "player_unavailable"],
            All.Select(s => s.Value()).Order().ToArray());
        Assert.Equal([Status.Pinned, Status.Personal], All.Where(s => s.HasTarget()).ToArray());
    }

    [Fact]
    public void ATargetStatusNeedsATarget()
    {
        foreach (var status in All.Where(s => s.HasTarget()))
        {
            Assert.Equal(60500L, new JumpResult(status, 60500).TargetMs);
            Assert.Throws<ArgumentException>(() => new JumpResult(status));
        }
    }

    [Fact]
    public void AStatusWithoutATargetRefusesOne()
    {
        foreach (var status in All.Where(s => !s.HasTarget()))
        {
            Assert.Null(new JumpResult(status).TargetMs);
            Assert.Throws<ArgumentException>(() => new JumpResult(status, 60500));
        }
    }

    [Fact]
    public void ATargetCannotBeNegative()
    {
        Assert.Throws<ArgumentException>(() => new JumpResult(Status.Personal, -1));
        Assert.Equal(0L, new JumpResult(Status.Personal, 0).TargetMs);
    }
}
