using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hookjump.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hookjump.Storage.Tests;

/// <summary>Local storage: pins, learned seeks, the 7-day samples and forgetting (spec/STORAGE.md).</summary>
public sealed class StoreTest : IDisposable
{
    private const long DayMs = 24L * 60 * 60 * 1000;
    private static readonly long[] Rewind = [130000, 131000, 105000, 106000]; // one rewind, 131000 -> 105000

    private readonly string _dir = Directory.CreateTempSubdirectory("hookjump-test-").FullName;
    private string _path;
    private Store _store;

    public StoreTest()
    {
        _path = Path.Combine(_dir, "hookjump.db");
        _store = new Store(_path);
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Feeds samples one second apart, as the player would report them.</summary>
    private sealed class Clock(Store store, long at = 1_700_000_000_000)
    {
        public List<long?> Play(string track, IEnumerable<long> positions) =>
            positions.Select(p =>
            {
                var id = store.AddSample(new PlaybackSample(at, track, true, p));
                at += 1000;
                return id;
            }).ToList();
    }

    /// <summary>Looks into the file through a second connection, as any reader would.</summary>
    private List<T> Look<T>(string sql, Func<SqliteDataReader, T> read)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(read(reader));
        return rows;
    }

    private long Count(string table) => Look($"SELECT COUNT(*) FROM {table}", r => r.GetInt64(0)).Single();

    private static List<long?> Nulls(int n) => Enumerable.Repeat<long?>(null, n).ToList();

    [Fact]
    public void PinsRoundTrip()
    {
        Assert.Null(_store.Pin("a"));
        _store.SetPin("a", new Pin(83000, Typed: true));
        Assert.Equal(new Pin(83000, true), _store.Pin("a"));
        _store.SetPin("a", new Pin(106200, Typed: false)); // a new pin replaces the old
        Assert.Equal(new Pin(106200, false), _store.Pin("a"));
        _store.ClearPin("a");
        Assert.Null(_store.Pin("a"));
    }

    [Fact]
    public void ListenNumbersCountPerTrack()
    {
        var clock = new Clock(_store);
        clock.Play("a", Rewind);
        clock.Play("b", Rewind);
        clock.Play("a", Rewind);
        Assert.Equal(3, _store.Process());
        Assert.Equal([1, 2], _store.Seeks("a").Select(s => s.Listen));
        Assert.Equal([1], _store.Seeks("b").Select(s => s.Listen));
        Assert.Equal((131000L, 105000L), (_store.Seeks("a")[0].FromMs, _store.Seeks("a")[0].ToMs));
    }

    [Fact]
    public void ListenNumbersContinueAcrossBatches()
    {
        var clock = new Clock(_store);
        clock.Play("a", Rewind);
        _store.Process();
        clock.Play("b", [5000, 6000]);
        clock.Play("a", Rewind);
        _store.Process();
        Assert.Equal([1, 2], _store.Seeks("a").Select(s => s.Listen));
    }

    [Fact]
    public void TheListenStillGoingOnWaits()
    {
        var clock = new Clock(_store);
        clock.Play("a", Rewind);
        var firstOfB = clock.Play("b", Rewind.Take(2))[0];
        Assert.Equal(1, _store.Process(beforeId: firstOfB)); // only track a's listen
        clock.Play("b", Rewind.Skip(2)); // the same listen of b goes on, then ends
        Assert.Equal(1, _store.Process());
        Assert.Equal([1], _store.Seeks("b").Select(s => s.Listen)); // one listen, not two
    }

    [Fact]
    public void ASeekRightAtTheStartOfTheWaitingListenIsKept()
    {
        // Cutting the listen after its first sample would lose this seek.
        var clock = new Clock(_store);
        clock.Play("a", Rewind);
        var firstOfB = clock.Play("b", [130000])[0];
        _store.Process(beforeId: firstOfB);
        clock.Play("b", [50000, 51000]); // skipped back just after b began
        _store.Process();
        Assert.Equal([(130000L, 50000L)], _store.Seeks("b").Select(s => (s.FromMs, s.ToMs)));
    }

    [Fact]
    public void OnlyTheRecentListensWithSeeksAreKept()
    {
        var clock = new Clock(_store);
        for (var i = 0; i < Heatmap.RecentListens + 2; i++)
        {
            clock.Play("a", Rewind);
            clock.Play("b", [5000, 6000]); // a change of track ends each listen of a
        }
        _store.Process();
        Assert.Equal(Enumerable.Range(3, Heatmap.RecentListens), _store.Seeks("a").Select(s => s.Listen));
    }

    [Fact]
    public void PausedLearningWritesNothing()
    {
        _store.SetLearningPaused(true);
        Assert.Equal(Nulls(4), new Clock(_store).Play("a", Rewind));
        Assert.Equal(0L, Count("samples"));
        _store.SetLearningPaused(false);
        Assert.DoesNotContain(null, new Clock(_store).Play("a", Rewind));
    }

    [Fact]
    public void AnIgnoredTrackIsForgottenAndNotLearnedButKeepsItsPin()
    {
        var clock = new Clock(_store);
        clock.Play("a", Rewind);
        _store.Process();
        _store.SetPin("a", new Pin(50000, Typed: true));
        _store.Ignore("a");
        Assert.True(_store.IsIgnored("a"));
        Assert.Empty(_store.Seeks("a"));
        Assert.Equal(new Pin(50000, true), _store.Pin("a"));
        Assert.Equal(Nulls(4), clock.Play("a", Rewind));
        Assert.DoesNotContain(null, clock.Play("b", Rewind)); // other tracks are still learned
        _store.Unignore("a");
        Assert.DoesNotContain(null, clock.Play("a", Rewind));
    }

    [Fact]
    public void ForgetTrackTouchesOnlyThatTrack()
    {
        var clock = new Clock(_store);
        clock.Play("a", Rewind);
        clock.Play("b", Rewind);
        _store.Process();
        _store.SetPin("a", new Pin(1000, false));
        _store.SetPin("b", new Pin(2000, false));
        _store.ForgetTrack("a");
        Assert.Null(_store.Pin("a"));
        Assert.Empty(_store.Seeks("a"));
        Assert.Equal(new Pin(2000, false), _store.Pin("b"));
        Assert.Single(_store.Seeks("b"));
        Assert.Equal(["b"], Look("SELECT DISTINCT track_id FROM samples", r => r.GetString(0)));
    }

    [Fact]
    public void ForgetLearnedOfOneTrackTouchesOnlyThatTrackAndLearningGoesOn()
    {
        var clock = new Clock(_store);
        clock.Play("a", Rewind);
        clock.Play("b", Rewind);
        _store.Process();
        _store.ForgetLearned("a");
        Assert.Empty(_store.Seeks("a"));
        Assert.Single(_store.Seeks("b"));
        Assert.Equal(["b"], Look("SELECT DISTINCT track_id FROM samples", r => r.GetString(0)));
        Assert.DoesNotContain(null, clock.Play("a", Rewind)); // still learned from
        Assert.Equal(1, _store.Process());
        Assert.Single(_store.Seeks("a"));
    }

    [Fact]
    public void ForgetLearnedOfOneTrackKeepsItsPinItsIgnoredMarkAndTheirNames()
    {
        var clock = new Clock(_store);
        foreach (var track in new[] { "a", "b", "c" }) clock.Play(track, Rewind);
        _store.Process();
        _store.SetPin("b", new Pin(1000, false));
        _store.Ignore("c");
        Assert.True(_store.Name("a", "A", null) && _store.Name("b", "B", null) && _store.Name("c", "C", null));
        foreach (var track in new[] { "a", "b", "c" }) _store.ForgetLearned(track);
        Assert.Equal((0L, 0L), (Count("seeks"), Count("samples")));
        Assert.Equal(new Pin(1000, false), _store.Pin("b"));
        Assert.True(_store.IsIgnored("c"));
        Assert.Equal(["b", "c"], Names().Select(n => n.Track)); // a was named only for what it taught
    }

    [Fact]
    public void ForgetLearnedKeepsPinsIgnoredTracksAndSettings()
    {
        new Clock(_store).Play("a", Rewind);
        _store.Process();
        _store.SetPin("a", new Pin(1000, true));
        _store.Ignore("c");
        _store.SetLearningPaused(true);
        _store.ForgetLearned();
        Assert.Equal((0L, 0L), (Count("seeks"), Count("samples")));
        Assert.Equal(new Pin(1000, true), _store.Pin("a"));
        Assert.True(_store.IsIgnored("c") && _store.LearningPaused());
    }

    [Fact]
    public void ShortcutsAreKeptAndZeroIsTheDefault()
    {
        Assert.Equal(0, _store.Shortcut("jump")); // nothing kept yet
        _store.SetShortcut("jump", ('J' << 16) | 0x3);
        Assert.Equal(('J' << 16) | 0x3, _store.Shortcut("jump"));
        Assert.Equal(0, _store.Shortcut("pin")); // each its own
        Assert.Equal([("jump_shortcut", ('J' << 16) | 0x3L)], Look(
            "SELECT key, value FROM settings WHERE key LIKE '%shortcut'", r => (r.GetString(0), r.GetInt64(1))));
        _store.SetShortcut("jump", 0);
        Assert.Equal(0, _store.Shortcut("jump"));
    }

    [Fact]
    public void ForgetAllLeavesAnEmptyWorkingStore()
    {
        var clock = new Clock(_store);
        clock.Play("a", Rewind);
        _store.Process();
        _store.SetPin("a", new Pin(1000, true));
        _store.Ignore("c");
        _store.SetLearningPaused(true);
        _store.ForgetAll();
        foreach (var table in new[] { "pins", "seeks", "samples", "ignored", "settings", "tracks" })
            Assert.True(Count(table) == 0, table);
        Assert.False(_store.LearningPaused());
        clock.Play("a", Rewind);
        Assert.Equal(1, _store.Process()); // the processed mark started over with it
    }

    [Fact]
    public void SampleIdsAreNeverReused()
    {
        // Reused ids would sit below the processed mark and be skipped for good.
        var clock = new Clock(_store);
        var old = clock.Play("a", Rewind).OfType<long>().ToList();
        _store.Process();
        _store.ForgetLearned();
        var fresh = clock.Play("a", Rewind).OfType<long>().ToList();
        Assert.True(fresh.Min() > old.Max());
        Assert.Equal(1, _store.Process());
    }

    [Fact]
    public void PurgeDropsOnlyOldProcessedSamples()
    {
        const long now = 1_700_000_000_000;
        var old = new Clock(_store, now - (Store.SampleDays + 1) * DayMs);
        old.Play("a", Rewind);
        _store.Process();
        old.Play("b", Rewind); // old but not processed yet: must survive
        new Clock(_store, now - DayMs).Play("c", Rewind); // recent
        Assert.Equal(4, _store.PurgeSamples(now));
        Assert.Equal(["b", "c"], Look("SELECT DISTINCT track_id FROM samples ORDER BY 1", r => r.GetString(0)));
    }

    [Fact]
    public void DataSurvivesReopening()
    {
        var nested = Path.Combine(_dir, "sub", "hookjump.db"); // the folder is created
        using (var first = new Store(nested)) first.SetPin("a", new Pin(1000, true));
        using var second = new Store(nested);
        Assert.Equal(new Pin(1000, true), second.Pin("a"));
    }

    [Fact]
    public void ANewerSchemaIsRefused()
    {
        _store.Dispose();
        Look("PRAGMA user_version = 3", _ => 0);
        var error = Assert.Throws<InvalidOperationException>(() => new Store(_path));
        Assert.Contains("schema version 3", error.Message);
        _store = new Store(Path.Combine(_dir, "other.db")); // for Dispose()
    }

    [Fact]
    public void DeletedDataIsOverwrittenInTheFile()
    {
        _store.SetPin("smtc:FORGETME0123456789", new Pin(1000, true));
        _store.ForgetTrack("smtc:FORGETME0123456789");
        _store.Dispose();
        var bytes = Encoding.Latin1.GetString(File.ReadAllBytes(_path));
        Assert.False(bytes.Contains("FORGETME0123456789"), "a forgotten track id is still readable in the file");
        _store = new Store(_path);
    }

    [Fact]
    public void TheFileMatchesTheMacsFormat()
    {
        Assert.Equal([Store.SchemaVersion], Look("PRAGMA user_version", r => r.GetInt64(0)));
        Assert.Equal(
            ["ignored", "pins", "samples", "seeks", "settings", "sqlite_sequence", "tracks"],
            Look("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name", r => r.GetString(0)));
    }

    [Fact]
    public void FromSamplesToAJump()
    {
        // Two listens rewinding to 1:45: the next jump lands 1.5 s before it.
        var clock = new Clock(_store);
        for (var i = 0; i < 2; i++)
        {
            clock.Play("a", Rewind);
            clock.Play("b", [5000, 6000]);
        }
        _store.Process();
        var result = Target.Choose(trackLoaded: true, pin: null, favorite: Heatmap.Favorite(_store.Seeks("a")));
        Assert.Equal((Status.Personal, (long?)103500), (result.Status, result.TargetMs));
    }

    private List<(string Track, string? Title, string? Artist)> Names() => Look(
        "SELECT track_id, title, artist FROM tracks ORDER BY track_id",
        r => (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));

    [Fact]
    public void ASongOnlyListenedToIsNeverNamed()
    {
        new Clock(_store).Play("a", [5000, 6000, 7000]);
        _store.Process();
        Assert.False(_store.Name("a", "Song", "Artist"));
        Assert.Empty(Names());
    }

    [Fact]
    public void ANameLivesAsLongAsSomethingElseIsKept()
    {
        _store.SetPin("a", new Pin(1000, false));
        Assert.True(_store.Name("a", "Song", "Artist"));
        Assert.Equal([("a", "Song", "Artist")], Names());
        _store.Name("a", "Song", "Artist (live)"); // a changed name is written
        Assert.Equal([("a", "Song", "Artist (live)")], Names());
        _store.ClearPin("a");
        Assert.Empty(Names());
    }

    [Fact]
    public void NamesFollowSeeksIgnoringAndForgetting()
    {
        var clock = new Clock(_store);
        clock.Play("a", Rewind);
        clock.Play("b", Rewind);
        _store.Process();
        Assert.True(_store.Name("a", "A", null) && _store.Name("b", "B", null));
        _store.Ignore("a"); // seeks go, the ignored row keeps the name
        Assert.Equal(["a", "b"], Names().Select(n => n.Track));
        _store.Unignore("a");
        Assert.Equal(["b"], Names().Select(n => n.Track));
        _store.ForgetLearned();
        Assert.Empty(Names());
    }

    [Fact]
    public void ForgettingASongForgetsItsNameUnlessItIsIgnored()
    {
        _store.SetPin("a", new Pin(1000, false));
        _store.SetPin("b", new Pin(1000, false));
        _store.Ignore("b");
        _store.Name("a", "A", null);
        _store.Name("b", "B", null);
        _store.ForgetTrack("a");
        _store.ForgetTrack("b");
        Assert.Equal(["b"], Names().Select(n => n.Track)); // still not tracked, so still shown
    }

    [Fact]
    public void WhatIsKeptIsListedNamedFirst()
    {
        new Clock(_store).Play("z", Rewind);
        _store.Process();
        _store.SetPin("a", new Pin(83000, true));
        _store.Ignore("m");
        _store.Name("a", "beta", "X");
        _store.Name("m", "Alpha", null);
        var kept = _store.KeptSongs();
        Assert.Equal(["m", "a", "z"], kept.Select(k => k.TrackId)); // by title, unnamed last
        Assert.Equal(new Kept("m", "Alpha", null, null, true), kept[0]);
        Assert.Equal(new Pin(83000, true), kept[1].Pin);
        Assert.Null(kept[2].Title);
    }

    [Fact]
    public void AVersion1FileIsUpgradedInPlace()
    {
        _store.Dispose();
        _path = Path.Combine(_dir, "v1.db");
        // The version 1 tables, as spec/STORAGE.md had them.
        foreach (var sql in new[]
        {
            "CREATE TABLE pins (track_id TEXT PRIMARY KEY, at_ms INTEGER NOT NULL CHECK (at_ms >= 0), typed INTEGER NOT NULL CHECK (typed IN (0, 1)))",
            "CREATE TABLE seeks (track_id TEXT NOT NULL, listen INTEGER NOT NULL, from_ms INTEGER NOT NULL, to_ms INTEGER NOT NULL)",
            "CREATE INDEX seeks_by_track ON seeks (track_id, listen)",
            "CREATE TABLE samples (id INTEGER PRIMARY KEY AUTOINCREMENT, at_ms INTEGER NOT NULL, track_id TEXT, playing INTEGER NOT NULL, position_ms INTEGER, own_seek INTEGER NOT NULL)",
            "CREATE TABLE ignored (track_id TEXT PRIMARY KEY)",
            "CREATE TABLE settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL)",
            "INSERT INTO pins VALUES ('a', 1000, 1)",
            "PRAGMA user_version = 1",
        })
        {
            Look(sql, _ => 0);
        }
        _store = new Store(_path);
        Assert.Equal(new Pin(1000, true), _store.Pin("a"));
        Assert.True(_store.Name("a", "Song", null));
        Assert.Equal([Store.SchemaVersion], Look("PRAGMA user_version", r => r.GetInt64(0)));
    }
}
