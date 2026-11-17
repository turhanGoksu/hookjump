using System;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Hookjump.Core.Tests;

/// <summary>The listener's favorite part, checked against spec/vectors/heatmap_favorite.json.</summary>
public class HeatmapTest
{
    public static TheoryData<VectorCase> Cases => SpecVectors.Cases("heatmap_favorite.json");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Favorite(VectorCase @case)
    {
        var seeks = @case.Data["seeks"]!.AsArray().Select(node => node!.AsObject()).Select(s =>
            new SeekEvent("a", (int)s["listen"]!, (long)s["from_ms"]!, (long)s["to_ms"]!)).ToList();
        var favorite = Heatmap.Favorite(seeks);
        JsonNode? found = favorite is null ? null : new JsonObject
        {
            ["target_ms"] = favorite.TargetMs,
            ["points"] = favorite.Points,
            ["listens"] = favorite.Listens,
        };
        Assert.Equal(SpecVectors.Canonical(@case.Data["expected"]), SpecVectors.Canonical(found));
    }

    [Fact]
    public void SeeksOfSeveralTracksAreRefused()
    {
        SeekEvent[] seeks = [new("a", 1, 9000, 1000), new("b", 1, 9000, 1000)];
        Assert.Throws<ArgumentException>(() => Heatmap.Favorite(seeks));
    }
}
