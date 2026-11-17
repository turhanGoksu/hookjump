using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Hookjump.Core.Tests;

/// <summary>
/// The core stays portable: it uses only plain computation from .NET itself.
/// </summary>
/// <remarks>
/// No files, network, clock, threads, Windows or a player: the core receives
/// data as arguments and returns data, like src/hookjump/core in Python. The
/// compiled core is read, not its sources, so a fully written name such as
/// System.IO.File cannot slip past a check of the usings.
/// </remarks>
public class PurityTest
{
    private static readonly string[] AllowedAssemblies = ["System.Runtime", "System.Collections", "System.Linq"];

    private static readonly string[] AllowedNamespaces =
    [
        "System", "System.Collections", "System.Collections.Generic", "System.Linq",
        "System.Runtime.CompilerServices", "System.Text",
    ];

    // In the System namespace itself, the ways out to the machine.
    private static readonly string[] ForbiddenSystemTypes =
    [
        "Activator", "AppContext", "AppDomain", "Console", "DateTime", "DateTimeOffset", "Environment", "GC",
        "Random", "TimeProvider",
    ];

    // Marks the compiler and the SDK put on every assembly, from namespaces
    // that otherwise hold ways out (System.Diagnostics also has Process and Stopwatch).
    private static readonly string[] AllowedMarks =
    [
        "System.Diagnostics.DebuggableAttribute", "System.Diagnostics.DebuggableAttribute+DebuggingModes",
        "System.Diagnostics.DebuggerBrowsableAttribute", "System.Diagnostics.DebuggerBrowsableState",
        "System.Reflection.AssemblyCompanyAttribute", "System.Reflection.AssemblyConfigurationAttribute",
        "System.Reflection.AssemblyFileVersionAttribute", "System.Reflection.AssemblyInformationalVersionAttribute",
        "System.Reflection.AssemblyProductAttribute", "System.Reflection.AssemblyTitleAttribute",
        "System.Runtime.Versioning.TargetFrameworkAttribute",
    ];

    [Fact]
    public void TheCoreUsesOnlyPlainComputation()
    {
        Assert.Equal([], Bad(typeof(Pin).Assembly.Location));
    }

    [Fact]
    public void TheCheckCatchesFilesAndOtherAssemblies()
    {
        // Guard the guard: a check that never fails proves nothing. This test
        // assembly reads files and uses System.Reflection.Metadata.
        var found = Bad(typeof(PurityTest).Assembly.Location);
        Assert.Contains("type System.IO.File", found);
        Assert.Contains("assembly System.Reflection.Metadata", found);
    }

    private static List<string> Bad(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var bad = new List<string>();
        foreach (var handle in md.AssemblyReferences)
        {
            var name = md.GetString(md.GetAssemblyReference(handle).Name);
            if (!AllowedAssemblies.Contains(name)) bad.Add($"assembly {name}");
        }
        foreach (var handle in md.TypeReferences)
        {
            var (ns, name) = FullName(md, handle);
            var outermost = name.Split('+')[0];
            var allowed = AllowedMarks.Contains($"{ns}.{name}") || (AllowedNamespaces.Contains(ns) &&
                !(ns == "System" && ForbiddenSystemTypes.Contains(outermost)));
            if (!allowed) bad.Add($"type {ns}.{name}");
        }
        return bad.Distinct().Order().ToList();
    }

    // A nested type's reference carries no namespace: it is its outer type's.
    private static (string Namespace, string Name) FullName(MetadataReader md, TypeReferenceHandle handle)
    {
        var type = md.GetTypeReference(handle);
        var name = md.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var (ns, outer) = FullName(md, (TypeReferenceHandle)type.ResolutionScope);
            return (ns, $"{outer}+{name}");
        }
        return (md.GetString(type.Namespace), name);
    }
}
