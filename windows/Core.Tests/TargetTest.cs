using System.Text.Json.Nodes;
using Xunit;

namespace Hookjump.Core.Tests;

/// <summary>Choosing the jump, checked against spec/vectors/target_choose.json.</summary>
public class TargetTest
{
    public static TheoryData<VectorCase> Cases => SpecVectors.Cases("target_choose.json");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Choose(VectorCase @case)
    {
        var data = @case.Data;
        Pin? pin = data["pin"] is JsonObject p ? new Pin((long)p["at_ms"]!, (bool)p["typed"]!) : null;
        Favorite? favorite = data["favorite"] is JsonObject f
            ? new Favorite((long)f["target_ms"]!, (int)f["points"]!, (int)f["listens"]!)
            : null;
        var result = Target.Choose((bool)data["track_loaded"]!, pin, favorite);
        var found = new JsonObject { ["status"] = result.Status.Value(), ["target_ms"] = result.TargetMs };
        Assert.Equal(SpecVectors.Canonical(data["expected"]), SpecVectors.Canonical(found));
    }
}
