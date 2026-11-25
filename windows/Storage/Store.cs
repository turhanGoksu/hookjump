using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hookjump.Core;
using Microsoft.Data.Sqlite;

namespace Hookjump.Storage;

/// <summary>One song something is kept about, for the listener to see.</summary>
public sealed record Kept(
    string TrackId,
    string? Title, // null when the song was not heard since it was kept
    string? Artist,
    Pin? Pin,
    bool Ignored);

/// <summary>
/// Local SQLite storage for what hookjump learns (spec/STORAGE.md), the same file
/// format as src/hookjump/adapters/storage.py on the Mac and android/storage.
/// </summary>
/// <remarks>
/// Raw samples are kept for 7 days; the seeks found in them and the listener's
/// pins are kept for good. The kept tables hold no dates or times.
/// </remarks>
public sealed class Store : IDisposable
{
    public const long SchemaVersion = 2;
    public const int SampleDays = 7;
    private const long DayMs = 24L * 60 * 60 * 1000;

    private static readonly string[] Schema =
    [
        """
        CREATE TABLE pins (
            track_id TEXT PRIMARY KEY,
            at_ms INTEGER NOT NULL CHECK (at_ms >= 0),
            typed INTEGER NOT NULL CHECK (typed IN (0, 1))
        )
        """,
        """
        CREATE TABLE seeks (
            track_id TEXT NOT NULL,
            listen INTEGER NOT NULL,
            from_ms INTEGER NOT NULL,
            to_ms INTEGER NOT NULL
        )
        """,
        "CREATE INDEX seeks_by_track ON seeks (track_id, listen)",
        """
        CREATE TABLE samples (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            at_ms INTEGER NOT NULL,
            track_id TEXT,
            playing INTEGER NOT NULL,
            position_ms INTEGER,
            own_seek INTEGER NOT NULL
        )
        """,
        "CREATE TABLE ignored (track_id TEXT PRIMARY KEY)",
        "CREATE TABLE settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL)",
    ];

    // Version 2 adds the names of the songs something is kept about.
    private const string Tracks = "CREATE TABLE tracks (track_id TEXT PRIMARY KEY, title TEXT, artist TEXT)";

    // Every track something is kept about; a name alone keeps nothing.
    private const string KeptIds =
        "SELECT track_id FROM pins UNION SELECT track_id FROM seeks UNION SELECT track_id FROM ignored";

    private readonly SqliteConnection _db;

    /// <summary>Opens (or creates) the hookjump database at <paramref name="path"/>.</summary>
    public Store(string path)
    {
        if (path != ":memory:")
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (folder is not null) Directory.CreateDirectory(folder);
        }
        // No pooling: closing the store closes the file, so nothing holds it open.
        var connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false };
        _db = new SqliteConnection(connection.ToString());
        _db.Open();
        // Deleted rows are overwritten, not left readable in free pages.
        Exec("PRAGMA secure_delete = ON");
        var version = One("PRAGMA user_version", r => r.GetInt64(0));
        if (version == 0)
        {
            Transaction(() =>
            {
                foreach (var sql in Schema.Append(Tracks)) Exec(sql);
                Exec($"PRAGMA user_version = {SchemaVersion}");
            });
        }
        else if (version == 1) // upgrade in place: only the names are new
        {
            Transaction(() =>
            {
                Exec(Tracks);
                Exec($"PRAGMA user_version = {SchemaVersion}");
            });
        }
        else if (version != SchemaVersion)
        {
            _db.Dispose();
            throw new InvalidOperationException(
                $"{path} has schema version {version}; this hookjump reads version {SchemaVersion}");
        }
    }

    public void Dispose() => _db.Dispose();

    // Samples and seeks ------------------------------------------------------

    /// <summary>Record one sample; its <c>AtMs</c> is Unix time in milliseconds.</summary>
    /// <remarks>Nothing is written while learning is paused or for an ignored track.</remarks>
    /// <returns>The sample's id, or null if it was not written.</returns>
    public long? AddSample(PlaybackSample sample)
    {
        if (LearningPaused() || (sample.TrackId is { } track && IsIgnored(track))) return null;
        return Transaction(() =>
        {
            Exec(
                "INSERT INTO samples (at_ms, track_id, playing, position_ms, own_seek) VALUES ($1, $2, $3, $4, $5)",
                sample.AtMs, sample.TrackId, sample.Playing, sample.PositionMs, sample.OwnSeek);
            return One("SELECT last_insert_rowid()", r => r.GetInt64(0));
        });
    }

    /// <summary>Find the seeks in the samples not processed yet and keep them.</summary>
    /// <remarks>
    /// Pass the id of the first sample of the listen still going on, so a listen
    /// is never cut in two (it would count as two listens); pass null when
    /// nothing is playing any more, such as at start-up.
    /// </remarks>
    /// <returns>The number of seeks kept.</returns>
    public int Process(long? beforeId = null)
    {
        var done = Setting("processed_sample_id");
        var rows = Query(
            "SELECT id, at_ms, track_id, playing, position_ms, own_seek FROM samples" +
            " WHERE id > $1 AND ($2 IS NULL OR id < $2) ORDER BY id",
            r => (Id: r.GetInt64(0), Sample: new PlaybackSample(
                AtMs: r.GetInt64(1),
                TrackId: r.IsDBNull(2) ? null : r.GetString(2),
                Playing: r.GetInt64(3) != 0,
                PositionMs: r.IsDBNull(4) ? null : r.GetInt64(4),
                OwnSeek: r.GetInt64(5) != 0)),
            done, beforeId);
        if (rows.Count == 0) return 0;
        var seeks = Seek.Detect(rows.Select(r => r.Sample)).OfType<SeekEvent>().ToList();

        // Detect() numbers listens within this batch. Kept listen numbers count
        // per track, only for listens with seeks, and only ever grow. Each seek
        // is inserted at once, so the next listen of the track counts on from it.
        var numbers = new Dictionary<(string Track, int Listen), long>();
        Transaction(() =>
        {
            foreach (var seek in seeks)
            {
                if (!numbers.TryGetValue((seek.TrackId, seek.Listen), out var number))
                {
                    number = LastListen(seek.TrackId) + 1;
                    numbers[(seek.TrackId, seek.Listen)] = number;
                }
                Exec(
                    "INSERT INTO seeks (track_id, listen, from_ms, to_ms) VALUES ($1, $2, $3, $4)",
                    seek.TrackId, number, seek.FromMs, seek.ToMs);
            }
            foreach (var track in seeks.Select(s => s.TrackId).Distinct()) KeepRecent(track);
            SetSetting("processed_sample_id", rows[^1].Id);
        });
        return seeks.Count;
    }

    public List<SeekEvent> Seeks(string trackId) => Query(
        "SELECT listen, from_ms, to_ms FROM seeks WHERE track_id = $1 ORDER BY listen, rowid",
        r => new SeekEvent(trackId, (int)r.GetInt64(0), r.GetInt64(1), r.GetInt64(2)),
        trackId);

    /// <summary>Delete processed samples older than <see cref="SampleDays"/>; returns how many.</summary>
    public int PurgeSamples(long nowMs) => Transaction(() => Exec(
        "DELETE FROM samples WHERE at_ms < $1 AND id <= $2",
        nowMs - SampleDays * DayMs, Setting("processed_sample_id")));

    // Pins -------------------------------------------------------------------

    public Pin? Pin(string trackId) => One(
        "SELECT at_ms, typed FROM pins WHERE track_id = $1",
        r => new Pin(r.GetInt64(0), r.GetInt64(1) != 0),
        trackId);

    public void SetPin(string trackId, Pin pin) => Transaction(() => Exec(
        "INSERT OR REPLACE INTO pins (track_id, at_ms, typed) VALUES ($1, $2, $3)",
        trackId, pin.AtMs, pin.Typed));

    public void ClearPin(string trackId) => Transaction(() =>
    {
        Exec("DELETE FROM pins WHERE track_id = $1", trackId);
        DropUnkeptNames();
    });

    // Names (spec/STORAGE.md, rule 6) ----------------------------------------

    /// <summary>Keep a song's name while something else is kept about it.</summary>
    /// <returns>
    /// True if it is kept now; nothing is written for a song only listened to,
    /// or when the name is already there.
    /// </returns>
    public bool Name(string trackId, string? title, string? artist)
    {
        if (title is null && artist is null) return false;
        if (!Any($"SELECT 1 FROM ({KeptIds}) WHERE track_id = $1", trackId)) return false;
        var stored = Query(
            "SELECT title, artist FROM tracks WHERE track_id = $1",
            r => (Title: r.IsDBNull(0) ? null : r.GetString(0), Artist: r.IsDBNull(1) ? null : r.GetString(1)),
            trackId);
        if (stored.Count == 0 || stored[0] != (title, artist))
        {
            Transaction(() => Exec(
                "INSERT OR REPLACE INTO tracks (track_id, title, artist) VALUES ($1, $2, $3)",
                trackId, title, artist));
        }
        return true;
    }

    /// <summary>Every song something is kept about, named ones first, by title.</summary>
    public List<Kept> KeptSongs() => Query(
        "SELECT k.track_id, t.title, t.artist, p.at_ms, p.typed, k.track_id IN (SELECT track_id FROM ignored)" +
        $" FROM ({KeptIds}) AS k LEFT JOIN tracks AS t USING (track_id) LEFT JOIN pins AS p USING (track_id)" +
        " ORDER BY t.title IS NULL, t.title COLLATE NOCASE, k.track_id",
        r => new Kept(
            TrackId: r.GetString(0),
            Title: r.IsDBNull(1) ? null : r.GetString(1),
            Artist: r.IsDBNull(2) ? null : r.GetString(2),
            Pin: r.IsDBNull(3) ? null : new Pin(r.GetInt64(3), r.GetInt64(4) != 0),
            Ignored: r.GetInt64(5) != 0));

    // Privacy ----------------------------------------------------------------

    public bool LearningPaused() => Setting("learning_paused") != 0;

    public void SetLearningPaused(bool paused) =>
        Transaction(() => SetSetting("learning_paused", paused ? 1 : 0));

    /// <summary>A stored shortcut, "jump" or "pin", as the app writes it; 0 for the default.</summary>
    public long Shortcut(string name) => Setting($"{name}_shortcut");

    public void SetShortcut(string name, long value) => Transaction(() => SetSetting($"{name}_shortcut", value));

    public bool IsIgnored(string trackId) => Any("SELECT 1 FROM ignored WHERE track_id = $1", trackId);

    /// <summary>Stop learning from a track and forget what was learned. Pins stay.</summary>
    public void Ignore(string trackId) => Transaction(() =>
    {
        Exec("INSERT OR IGNORE INTO ignored (track_id) VALUES ($1)", trackId);
        DeleteLearned(trackId);
    });

    public void Unignore(string trackId) => Transaction(() =>
    {
        Exec("DELETE FROM ignored WHERE track_id = $1", trackId);
        DropUnkeptNames();
    });

    /// <summary>Forget one track: its pin and everything learned from it.</summary>
    public void ForgetTrack(string trackId) => Transaction(() =>
    {
        Exec("DELETE FROM pins WHERE track_id = $1", trackId);
        DeleteLearned(trackId);
        DropUnkeptNames();
    });

    /// <summary>
    /// Forget what was learned from one track, which may be a spot the listener
    /// did not mean; its pin and its ignored mark stay, and learning goes on.
    /// </summary>
    public void ForgetLearned(string trackId) => Transaction(() =>
    {
        DeleteLearned(trackId);
        DropUnkeptNames();
    });

    /// <summary>Forget everything learned from listening; pins and settings stay.</summary>
    public void ForgetLearned() => Transaction(() =>
    {
        Exec("DELETE FROM seeks");
        Exec("DELETE FROM samples");
        DropUnkeptNames();
    });

    /// <summary>Delete everything: pins, learned data, ignored tracks, settings.</summary>
    public void ForgetAll()
    {
        Transaction(() =>
        {
            foreach (var table in new[] { "pins", "seeks", "samples", "ignored", "settings", "tracks" })
                Exec($"DELETE FROM {table}");
        });
        Exec("VACUUM"); // give the space back to the disk
    }

    // Internals --------------------------------------------------------------

    private long LastListen(string trackId) =>
        One("SELECT MAX(listen) FROM seeks WHERE track_id = $1", r => r.IsDBNull(0) ? 0 : r.GetInt64(0), trackId);

    // The heatmap only reads the RecentListens latest listens with seeks.
    private void KeepRecent(string trackId) =>
        Exec(
            "DELETE FROM seeks WHERE track_id = $1 AND listen NOT IN (" +
            " SELECT DISTINCT listen FROM seeks WHERE track_id = $1 ORDER BY listen DESC LIMIT $2)",
            trackId, Heatmap.RecentListens);

    // A name lives only as long as something else is kept about the song.
    private void DropUnkeptNames() => Exec($"DELETE FROM tracks WHERE track_id NOT IN ({KeptIds})");

    private void DeleteLearned(string trackId)
    {
        Exec("DELETE FROM seeks WHERE track_id = $1", trackId);
        Exec("DELETE FROM samples WHERE track_id = $1", trackId);
    }

    private long Setting(string key) => Query(
        "SELECT value FROM settings WHERE key = $1", r => r.GetInt64(0), key).FirstOrDefault();

    private void SetSetting(string key, long value) =>
        Exec("INSERT OR REPLACE INTO settings (key, value) VALUES ($1, $2)", key, value);

    private void Transaction(Action block) => Transaction(() =>
    {
        block();
        return 0;
    });

    private T Transaction<T>(Func<T> block)
    {
        Exec("BEGIN IMMEDIATE TRANSACTION");
        T result;
        try
        {
            result = block();
        }
        catch
        {
            Exec("ROLLBACK");
            throw;
        }
        Exec("COMMIT");
        return result;
    }

    /// <summary>Runs one statement; parameters are $1, $2... in order. Returns the rows changed.</summary>
    private int Exec(string sql, params object?[] args)
    {
        using var command = Command(sql, args);
        return command.ExecuteNonQuery();
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> row, params object?[] args)
    {
        using var command = Command(sql, args);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(row(reader));
        return rows;
    }

    // The first row, or the default (null for a pin) when there is none.
    private T? One<T>(string sql, Func<SqliteDataReader, T> row, params object?[] args) =>
        Query(sql, row, args).FirstOrDefault();

    private bool Any(string sql, params object?[] args) => Query(sql, _ => true, args).Count > 0;

    private SqliteCommand Command(string sql, object?[] args)
    {
        var command = _db.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < args.Length; i++)
        {
            command.Parameters.AddWithValue($"${i + 1}", args[i] switch
            {
                null => DBNull.Value,
                bool b => b ? 1L : 0L,
                int n => (long)n,
                long n => n,
                string s => s,
                var other => throw new ArgumentException($"cannot store a {other.GetType().Name}"),
            });
        }
        return command;
    }
}
