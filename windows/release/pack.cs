// Builds a Windows release of hookjump the same way here and in CI. In windows/:
//
//     dotnet run release/pack.cs -- 0.1.0
//
// It publishes the trimmed exe from the packages in the lock files, puts it in
// a zip with SQLite's library and the licenses (entries sorted, each dated at
// the commit's own time, so the same commit gives the same bytes), and writes
// the SHA-256 of the zip, the exe and the dll to SHA256SUMS.txt. Everything
// lands in windows/artifacts/.
#:property RestorePackagesWithLockFile=false

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 1 || !Regex.IsMatch(args[0], @"^\d+\.\d+\.\d+$"))
{
    Console.Error.WriteLine("usage, in windows/: dotnet run release/pack.cs -- <version, such as 0.1.0>");
    return 2;
}
var version = args[0];
var windows = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(windows, "Hookjump.slnx")))
{
    Console.Error.WriteLine("run it in windows/, where Hookjump.slnx is");
    return 2;
}
var repo = Path.GetFullPath(Path.Combine(windows, ".."));
var output = Path.Combine(windows, "artifacts");
var publish = Path.Combine(output, "publish");
if (Directory.Exists(output)) Directory.Delete(output, recursive: true);

// The commit's time dates every entry: the same commit, the same zip.
var commit = Run("git", "rev-parse", "HEAD");
var dated = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Run("git", "log", "-1", "--format=%ct")));
var dirty = Run("git", "status", "--porcelain").Length > 0;

Run(
    "dotnet", "publish", "App/App.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:RestoreLockedMode=true", "-p:ContinuousIntegrationBuild=true", $"-p:Version={version}", "-o", publish, "--nologo");

// Only what is meant to ship: a publish that grows another file must be looked at first.
var published = Directory.GetFiles(publish).Select(f => Path.GetFileName(f)).Where(f => !f.EndsWith(".pdb", StringComparison.Ordinal))
    .OrderBy(f => f, StringComparer.Ordinal).ToList();
if (!published.SequenceEqual(["e_sqlite3.dll", "hookjump.exe"]))
{
    Console.Error.WriteLine($"the publish holds {string.Join(", ", published)}, not just hookjump.exe and e_sqlite3.dll");
    return 1;
}

// The .NET runtime inside the exe ships its own license and notices; they come from the runtime pack it was built from.
var config = Directory.GetFiles(Path.Combine(windows, "App", "bin", "Release"), "hookjump.runtimeconfig.json", SearchOption.AllDirectories)
    .Single(f => f.Contains("win-x64", StringComparison.Ordinal));
using var json = JsonDocument.Parse(File.ReadAllText(config));
var runtime = json.RootElement.GetProperty("runtimeOptions").GetProperty("includedFrameworks").EnumerateArray()
    .Single(f => f.GetProperty("name").GetString() == "Microsoft.NETCore.App").GetProperty("version").GetString()!;
var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
var runtimePack = Path.Combine(packages, "microsoft.netcore.app.runtime.win-x64", runtime);
var fonts = Path.Combine(repo, "android", "app", "src", "main", "assets", "licenses");

var entries = new SortedDictionary<string, string>(StringComparer.Ordinal)
{
    ["hookjump.exe"] = Path.Combine(publish, "hookjump.exe"),
    ["e_sqlite3.dll"] = Path.Combine(publish, "e_sqlite3.dll"),
    ["LICENSE.txt"] = Path.Combine(repo, "LICENSE"),
    ["THIRD-PARTY-NOTICES.txt"] = Path.Combine(windows, "release", "THIRD-PARTY-NOTICES.txt"),
    ["licenses/BarlowCondensed-OFL.txt"] = Path.Combine(fonts, "BarlowCondensed-OFL.txt"),
    ["licenses/IBMPlexMono-OFL.txt"] = Path.Combine(fonts, "IBMPlexMono-OFL.txt"),
    ["licenses/dotnet-LICENSE.txt"] = Path.Combine(runtimePack, "LICENSE.TXT"),
    ["licenses/dotnet-THIRD-PARTY-NOTICES.txt"] = Path.Combine(runtimePack, "THIRD-PARTY-NOTICES.TXT"),
};

var zipName = $"hookjump-windows-{version}-x64.zip";
var zipPath = Path.Combine(output, zipName);
using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
{
    foreach (var (name, source) in entries)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(dated.UtcDateTime, TimeSpan.Zero); // the same on every clock
        using var into = entry.Open();
        using var from = File.OpenRead(source);
        from.CopyTo(into);
    }
}

var sums = string.Join("", new[] { (zipPath, zipName), (entries["hookjump.exe"], "hookjump.exe"), (entries["e_sqlite3.dll"], "e_sqlite3.dll") }
    .Select(f => $"{Sha256(f.Item1)}  {f.Item2}\n"));
File.WriteAllText(Path.Combine(output, "SHA256SUMS.txt"), sums);

Console.WriteLine($"hookjump {version} for Windows x64, from {commit}{(dirty ? " WITH UNCOMMITTED CHANGES: not for a release" : "")}");
Console.WriteLine($".NET runtime {runtime}; entries dated {dated.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
Console.WriteLine($"{zipName}: {new FileInfo(zipPath).Length:N0} bytes");
Console.Write(sums);
return 0;

static string Run(string file, params string[] arguments)
{
    var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, UseShellExecute = false };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var text = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{file} {string.Join(' ', arguments)} failed:\n{text}");
    return text.Trim();
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(SHA256.HashData(stream));
}
