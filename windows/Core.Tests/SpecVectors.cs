using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Sdk;

namespace Hookjump.Core.Tests;

/// <summary>Load the language-neutral test vectors in spec/vectors/, shared with Python and Kotlin.</summary>
public static class SpecVectors
{
    private static readonly string Dir = typeof(SpecVectors).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "SpecVectors").Value!;

    /// <summary>Every case of a vector file, each its own test named after the case.</summary>
    public static TheoryData<VectorCase> Cases(string file)
    {
        var data = new TheoryData<VectorCase>();
        foreach (var vectorCase in Load(file)) data.Add(vectorCase);
        return data;
    }

    public static IReadOnlyList<VectorCase> Load(string file)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, file)))!.AsObject();
        var cases = root["cases"]!.AsArray()
            .Select(node => node!.AsObject())
            .Select(data => new VectorCase(file, (string)data["name"]!, data))
            .ToList();
        var names = cases.Select(c => c.Name).ToList();
        if (names.Count != names.Distinct().Count())
            throw new InvalidDataException($"duplicate case names in {file}");
        return cases;
    }

    /// <summary>JSON text with object keys sorted, so a comparison ignores key order.</summary>
    public static string Canonical(JsonNode? node) => Sorted(node)?.ToJsonString() ?? "null";

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        null => null,
        JsonObject o => new JsonObject(o
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sorted).ToArray()),
        _ => node.DeepClone(),
    };
}

/// <summary>
/// One case of a vector file. xUnit stores only the file and the name, and
/// reads the case again from the file, so every case is listed as a test.
/// </summary>
public sealed class VectorCase : IXunitSerializable
{
    [Obsolete("For xUnit only.")]
    public VectorCase() { }

    public VectorCase(string file, string name, JsonObject data)
    {
        File = file;
        Name = name;
        Data = data;
    }

    public string File { get; private set; } = "";

    public string Name { get; private set; } = "";

    public JsonObject Data { get; private set; } = new();

    public override string ToString() => Name;

    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue("file", File);
        info.AddValue("name", Name);
    }

    public void Deserialize(IXunitSerializationInfo info)
    {
        File = info.GetValue<string>("file")!;
        Name = info.GetValue<string>("name")!;
        Data = SpecVectors.Load(File).Single(c => c.Name == Name).Data;
    }
}
