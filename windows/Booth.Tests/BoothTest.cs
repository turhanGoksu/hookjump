using System;
using System.IO;
using System.Linq;
using Hookjump.Core;
using Hookjump.Player;
using Hookjump.Player.Testing;
using Hookjump.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hookjump.Tests;

/// <summary>The booth, end to end on a fake turntable and a real store.</summary>
public sealed class BoothTest : IDisposable
{
    private const long HourMs = 60 * 60 * 1000;
    private const long DayMs = 24 * HourMs;
    private static readonly long[] Rewind = [130000, 131000, 105000, 106000]; // one rewind to 1:45

    private readonly string _dir = Directory.CreateTempSubdirectory("hookjump-test-").FullName;
    private readonly string _path;
    private readonly Store _store;
    private readonly FakeTurntable _player = new("a", 30000);
    private long _now = 1_700_000_000_000;
    private readonly Booth _booth;
    private Jumped? _jumped;

    public BoothTest()
    {
        _path = Path.Combine(_dir, "hookjump.db");
        _store = new Store(_path);
        _booth = new Booth(_store, _player, () => _now);
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>The player reports <paramref name="track"/> at each of these positions, one second apart.</summary>
    private void Play(string? track, params long[] positions)
    {
        _player.Track = track;
        foreach (var position in positions) Report(position);
    }

    /// <summary>One report, as Spotify sends when something changes.</summary>
    private void Report(long positionMs, bool playing = true)
    {
        _player.PositionMs = positionMs;
        _player.Playing = playing;
        _player.AtMs = _now;
        _booth.Heard();
        _now += 1000;
    }

    /// <summary>Spotify confirms a move a moment later.</summary>
    private void Confirm()
    {
        _player.AtMs = _now + 150;
        _booth.Heard();
    }

    private void Jump() => _booth.Jump(j => _jumped = j);

    private long Samples(string track)
    {
        using var db = new SqliteConnection($"Data Source={_path};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM samples WHERE track_id = $1";
        command.Parameters.AddWithValue("$1", track);
        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public void ARewindIsLearnedWhenTheTrackChanges()
    {
        Play("a", Rewind);
        Assert.Empty(_store.Seeks("a")); // the listen is still going on
        Play("b", 5000);
        Assert.Equal([105000L], _store.Seeks("a").Select(s => s.ToMs));
    }

    [Fact]
    public void ASkipRightAfterATrackStartsIsNotLost()
    {
        Play("a", 10000);
        Play("b", 0, 60000, 61000);
        Play("c", 0);
        Assert.Equal([60000L], _store.Seeks("b").Select(s => s.ToMs));
    }

    [Fact]
    public void MovesTakenInLateEachCountAtTheirOwnMoments()
    {
        // hookjump held back: a skip and a rewind 2 s apart are taken in
        // together, seconds later, each report as it was said.
        var start = _now;
        _now = start + 10000;
        foreach (var (atMs, positionMs) in new[] { (0L, 0L), (3000L, 130000L), (5000L, 105000L) })
            _booth.Heard(new Reading(start + atMs, "a", "Title", "Artist", 240000, true, positionMs));
        Play("b", 0);
        Assert.Equal([130000L, 105000L], _store.Seeks("a").Select(s => s.ToMs));
    }

    [Fact]
    public void SparseReportsStillLearnARewindAtItsMoment()
    {
        Report(30000); // playing from 0:30, then nothing for 70 s
        _now += 69000;
        Report(60000); // rewound to 1:00 from 1:40
        _now += 20000;
        Play("b", 0);
        Assert.Equal([new SeekEvent("a", 1, 99999, 60000)], _store.Seeks("a"));
    }

    [Fact]
    public void NothingKnownIsNoDataAndNothingMoves()
    {
        Play("a", 30000);
        Jump();
        Assert.Equal(new Jumped(new JumpResult(Status.NoData), null), _jumped);
        Assert.Equal(0, _player.Places);
        Assert.False(_booth.SpotKnown());
    }

    [Fact]
    public void TwoListensTeachTheFavoriteAndTheJumpLandsWhenSpotifyConfirms()
    {
        for (var i = 0; i < 2; i++)
        {
            Play("a", Rewind);
            Play("b", 5000, 6000);
        }
        Play("a", 30000);
        Assert.True(_booth.SpotKnown());
        Jump();
        Assert.Null(_jumped); // waiting for Spotify
        Confirm();
        Assert.Equal(new Jumped(new JumpResult(Status.Personal, 103500), Landing.Landed), _jumped);
        Assert.Equal(103500L, _player.PositionMs);
    }

    [Fact]
    public void APinWinsAndItsJumpIsNeverLearned()
    {
        Play("a", 50000);
        Assert.Equal(new Pin(51000, Typed: false), _booth.Pin().Pin); // a second has passed since the report
        Play("a", 80000); // a seek, but the pin will win
        Jump();
        Confirm();
        Assert.Equal(new Pin(51000, false), _store.Pin("a"));
        Assert.Equal(49500L, _player.PositionMs); // 1.5 s before the pin
        Assert.Equal(Landing.Landed, _jumped!.Landing);
        Play("a", 49500 + 1000L);
        Play("b", 0);
        Assert.Equal([80000L], _store.Seeks("a").Select(s => s.ToMs)); // the listener's seek only
    }

    [Fact]
    public void APinCountsTheTimeSinceTheLastReportOnlyWhilePlaying()
    {
        Report(60000);
        _now += 4000;
        Assert.Equal(65000L, _booth.Pin().Pin!.AtMs); // the report was 5 s ago
        Report(70000, playing: false);
        _now += 4000;
        Assert.Equal(70000L, _booth.Pin().Pin!.AtMs);
    }

    [Fact]
    public void TheDecisionTellsWhatAJumpWouldDoWithoutMoving()
    {
        Assert.Equal(Status.PlayerUnavailable, _booth.Decision().Status); // nothing heard yet
        Play("a", 30000);
        Assert.Equal(Status.NoData, _booth.Decision().Status);
        _booth.PinAt(83000);
        Assert.Equal(new JumpResult(Status.Pinned, 83000), _booth.Decision());
        Assert.Equal(0, _player.Places);
    }

    [Fact]
    public void TheChartShowsWhatWasLearnedOnThePlayingSong()
    {
        Assert.Null(_booth.PlayingChart()); // nothing heard yet
        for (var i = 0; i < 2; i++)
        {
            Play("a", Rewind);
            Play("b", 5000, 6000);
        }
        Play("a", 30000);
        var chart = _booth.PlayingChart()!;
        Assert.Equal(105000L, chart.HookMs);
        Assert.Equal([105000L, 105000L], chart.Rewinds);
    }

    [Fact]
    public void ARemovedPinGivesTheJumpBackToWhatWasLearned()
    {
        for (var i = 0; i < 2; i++)
        {
            Play("a", Rewind);
            Play("b", 5000, 6000);
        }
        Play("a", 30000);
        _booth.PinAt(60000);
        Assert.Equal(Status.Pinned, _booth.Decision().Status);
        Assert.True(_booth.Unpin());
        Assert.Null(_store.Pin("a"));
        Assert.Equal(new JumpResult(Status.Personal, 103500), _booth.Decision());
        Assert.False(_booth.Unpin()); // nothing left to remove
    }

    [Fact]
    public void ForgettingTheSongKeepsTheOthers()
    {
        for (var i = 0; i < 2; i++)
        {
            Play("a", Rewind);
            Play("b", Rewind);
        }
        Play("a", 30000);
        _booth.PinAt(60000);
        Assert.True(_booth.ForgetSong());
        Assert.Null(_store.Pin("a"));
        Assert.Empty(_store.Seeks("a"));
        Assert.Equal(2, _store.Seeks("b").Count);
    }

    [Fact]
    public void ForgettingWhatWasLearnedKeepsThePinAndLearnsAgain()
    {
        for (var i = 0; i < 2; i++)
        {
            Play("a", Rewind);
            Play("b", Rewind);
        }
        Play("a", 30000);
        Assert.Equal(new JumpResult(Status.Personal, 103500), _booth.Decision());
        Assert.True(_booth.ForgetLearnedFromSong());
        Assert.Equal(new JumpResult(Status.NoData), _booth.Decision()); // the learned spot is gone
        Assert.Equal(2, _store.Seeks("b").Count); // the other song keeps what it taught

        _booth.PinAt(60000);
        Assert.True(_booth.ForgetLearnedFromSong());
        Assert.Equal(new Pin(60000, Typed: true), _store.Pin("a")); // a pin is a choice, not learning
        _booth.Unpin();
        Play("b", 5000); // a's listen ends: learned again from here on
        Play("a", Rewind);
        Play("b", 5000);
        Assert.NotEmpty(_store.Seeks("a"));
    }

    [Fact]
    public void ForgettingAllThatWasLearnedKeepsEveryPin()
    {
        for (var i = 0; i < 2; i++)
        {
            Play("a", Rewind);
            Play("b", Rewind);
        }
        _booth.PinAt(1000); // b
        _booth.ForgetAllLearned();
        Assert.Empty(_store.Seeks("a"));
        Assert.Empty(_store.Seeks("b"));
        Assert.Equal(new Pin(1000, Typed: true), _store.Pin("b"));
    }

    [Fact]
    public void AnIgnoredSongKeepsItsPinLearnsNothingAndCanBeTrackedAgain()
    {
        Play("a", Rewind);
        Play("b", 5000);
        Play("a", 30000);
        _booth.PinAt(60000);
        Assert.True(_booth.IgnoreSong());
        var chart = _booth.PlayingChart()!;
        Assert.True(chart.Ignored);
        Assert.Equal(60000L, chart.HookMs); // the pin stays
        Assert.Empty(_store.Seeks("a"));
        Play("a", Rewind);
        Play("b", 5000);
        Assert.Empty(_store.Seeks("a")); // nothing learned while ignored
        Play("a", 30000);
        Assert.True(_booth.TrackSongAgain());
        Assert.False(_booth.PlayingChart()!.Ignored);
    }

    [Fact]
    public void PausedLearningShowsOnTheChartAndLearnsNothing()
    {
        _booth.SetLearningPaused(true);
        Play("a", Rewind);
        Play("b", 5000);
        Assert.Empty(_store.Seeks("a"));
        Assert.True(_booth.PlayingChart()!.LearningPaused);
        _booth.SetLearningPaused(false);
        Assert.False(_booth.PlayingChart()!.LearningPaused);
    }

    [Fact]
    public void ForgettingEverythingLeavesNothing()
    {
        for (var i = 0; i < 2; i++)
        {
            Play("a", Rewind);
            Play("b", Rewind);
        }
        _booth.PinAt(1000);
        _booth.SetLearningPaused(true);
        _booth.ForgetEverything();
        Assert.Null(_store.Pin("b"));
        Assert.Empty(_store.Seeks("a"));
        Assert.False(_store.LearningPaused());
    }

    [Fact]
    public void TheSongOnScreenIsActedOnEvenWhenThePlayerStopsListingIt()
    {
        // A player can drop a long-paused session without a word; the screen
        // still shows that song, and changes act on it.
        Play("a", 30000);
        _player.Running = false; // no report: Heard() is not called
        Assert.True(_booth.IgnoreSong());
        Assert.True(_store.IsIgnored("a"));
        Assert.Equal(new Pin(60000, Typed: true), _booth.PinAt(60000).Pin);
        Assert.True(_booth.Unpin());
    }

    [Fact]
    public void WithNothingPlayingThereIsNoSongToActOn()
    {
        _player.Running = false;
        _booth.Heard();
        Assert.False(_booth.ForgetSong());
        Assert.False(_booth.ForgetLearnedFromSong());
        Assert.False(_booth.IgnoreSong());
        Assert.False(_booth.TrackSongAgain());
    }

    [Fact]
    public void TheBoothNamesSongsOnlyOnceSomethingIsKept()
    {
        Play("a", Rewind); // a rewind: a's listen is learned when b starts
        Play("b", 5000, 6000); // only listened to
        Play("c", 1000);
        Assert.Equal(["Title"], _booth.Records().Select(r => r.Title));
        _booth.Pin(); // c, pinned while it plays
        Assert.Equal(2, _booth.Records().Count);
        Assert.Equal(["a", "c"], _store.KeptSongs().Select(k => k.TrackId).Order());
    }

    [Fact]
    public void RecordsTellWhatIsKeptAboutEachSong()
    {
        for (var i = 0; i < 2; i++)
        {
            Play("a", Rewind);
            Play("b", 5000, 6000);
        }
        Play("c", 1000);
        _booth.IgnoreSong();
        var records = _booth.Records().OrderBy(r => r.Ignored).ToList();
        Assert.Equal((2, 2, (long?)105000), (records[0].Seeks, records[0].Listens, records[0].Favorite?.TargetMs));
        Assert.True(records[1].Ignored);
        Assert.Equal("Title", records[1].Title); // a song not tracked is listed by name
    }

    [Fact]
    public void APinNearTheEndStaysWithinTheSong()
    {
        Report(239000); // the fake song is 4:00 long
        _now += 5000;
        Assert.Equal(240000L, _booth.Pin().Pin!.AtMs);
    }

    [Fact]
    public void ASilentSpotifyIsAskedOnceMoreThenTheJumpIsReportedAsSlipped()
    {
        // The regular check keeps reading the player; its old report is no
        // answer, but the time it brings lets the needle try again, then give up.
        Play("a", 30000);
        _booth.PinAt(83000);
        var before = _player.Read()!;
        _player.Ignores = 2;
        Jump();
        _now += Needle.SettleMs;
        _booth.Heard(before);
        Assert.Equal(2, _player.Places);
        Assert.Null(_jumped);
        _now += Needle.SettleMs;
        _booth.Heard(before);
        Assert.Equal(Landing.Slipped, _jumped!.Landing);
    }

    [Fact]
    public void ATypedPinIsExact()
    {
        Play("a", 30000);
        Assert.Equal(new Pin(83000, Typed: true), _booth.PinAt(83000).Pin);
        Jump();
        Confirm();
        Assert.Equal(83000L, _player.PositionMs);
    }

    [Fact]
    public void AJumpSpotifyIgnoresTwiceIsReportedAsSlipped()
    {
        Play("a", 30000);
        _booth.PinAt(83000);
        _player.Ignores = 2;
        Jump();
        Confirm();
        Confirm();
        Assert.Equal(Landing.Slipped, _jumped!.Landing);
        Assert.Equal(2, _player.Places);
    }

    [Fact]
    public void WithSpotifyClosedNothingHappensAndItSaysSo()
    {
        _player.Running = false;
        _booth.Heard();
        Jump();
        Assert.Equal(new Jumped(new JumpResult(Status.PlayerUnavailable), null), _jumped);
        Assert.Null(_booth.Pin().Pin);
        Assert.Null(_booth.PinAt(1000).Pin);
        Assert.Equal(0, _player.Places);
    }

    [Fact]
    public void LeftoversOfTheLastRunAreLearnedAtStart()
    {
        long[] positions = [130000, 131000, 105000, 106000];
        for (var i = 0; i < positions.Length; i++)
            _store.AddSample(new PlaybackSample(_now + i * 1000, "a", true, positions[i]));
        _ = new Booth(_store, _player, () => _now);
        Assert.Equal([105000L], _store.Seeks("a").Select(s => s.ToMs));
    }

    [Fact]
    public void OldSamplesArePurgedOnceAnHour()
    {
        var old = _now - 8 * DayMs;
        _store.AddSample(new PlaybackSample(old, "x", true, 1000));
        _store.Process();
        _booth.Heard(); // the first report purges
        Assert.Equal(0L, Samples("x"));
        _store.AddSample(new PlaybackSample(old, "y", true, 1000));
        _store.Process();
        _now += HourMs - 2000;
        _booth.Heard();
        Assert.Equal(1L, Samples("y")); // not within the hour
        _now += 2000;
        _booth.Heard();
        Assert.Equal(0L, Samples("y"));
    }
}
