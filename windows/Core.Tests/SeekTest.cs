using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Hookjump.Core.Tests;

/// <summary>Seek detection, checked against spec/vectors/seek_detect.json.</summary>
public class SeekTest
{
    public static TheoryData<VectorCase> Cases => SpecVectors.Cases("seek_detect.json");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Detect(VectorCase @case)
    {
        var samples = @case.Data["samples"]!.AsArray().Select(node => node!.AsObject()).Select(s =>
            new PlaybackSample(
                AtMs: (long)s["at_ms"]!,
                TrackId: (string?)s["track_id"],
                Playing: (bool)s["playing"]!,
                PositionMs: (long?)s["position_ms"],
                OwnSeek: (bool?)s["own_seek"] ?? false));
        var found = new JsonArray(Seek.Detect(samples).Select(e => (JsonNode)new JsonObject
        {
            ["type"] = e is PlaySpan ? "play" : "seek",
            ["track_id"] = e.TrackId,
            ["listen"] = e.Listen,
            ["from_ms"] = e.FromMs,
            ["to_ms"] = e.ToMs,
        }).ToArray());
        Assert.Equal(SpecVectors.Canonical(@case.Data["expected"]), SpecVectors.Canonical(found));
    }
}
