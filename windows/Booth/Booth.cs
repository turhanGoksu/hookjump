using System;
using System.Collections.Generic;
using System.Linq;
using Hookjump.Core;
using Hookjump.Player;
using Hookjump.Storage;

namespace Hookjump;

// The booth: where the turntable, the ear, the needle and the records meet.
//
// Every way of asking (a hotkey, the tray, the screen) ends here, so a jump or
// a pin goes through the same few lines however it was asked for. No UI and no
// player calls of its own: in tests it runs on a fake turntable.

/// <summary>A jump decision, and where the jump ended up: null when nothing was moved.</summary>
public sealed record Jumped(JumpResult Result, Landing? Landing);

/// <summary>The pin set, or null when nothing is playing, so nothing was pinned.</summary>
public sealed record Pinned(Pin? Pin);

/// <summary>One song in "what hookjump keeps": its name, and what is kept about it.</summary>
public sealed record Record(
    string? Title, // null when the song was not heard since it was kept
    string? Artist,
    Pin? Pin,
    Favorite? Favorite,
    int Seeks,
    int Listens,
    bool Ignored);

public sealed class Booth
{
    public const long PurgeEveryMs = 60 * 60 * 1000;
    private const int NamesRemembered = 50;

    private readonly Store _store;
    private readonly ITurntable _turntable;
    private readonly Func<long> _clock; // Unix time in milliseconds
    private readonly Ear _ear = new();
    private readonly Needle _needle;
    private string? _track; // the track of the listen going on
    private long _purgedAtMs;

    // Names heard this session: a song's listen is processed after it is over,
    // when the player may already report the next song. Memory only; the
    // oldest heard goes first.
    private readonly OrderedDictionary<string, (string? Title, string? Artist)> _names = new();

    public Booth(Store store, ITurntable turntable, Func<long> clock)
    {
        _store = store;
        _turntable = turntable;
        _clock = clock;
        _needle = new Needle(turntable, _ear);
        // Samples left from the last run belong to listens that are over.
        store.Process();
    }

    /// <summary>The player's latest report, for the screen.</summary>
    public Reading? Reading { get; private set; }

    /// <summary>A regular check came round: take in the player's state now.</summary>
    public void Heard() => Heard(_turntable.Read());

    /// <summary>The player reported a change: take in <paramref name="latest"/>.</summary>
    public void Heard(Reading? latest)
    {
        var now = _clock();
        Reading = latest;
        if (latest?.TrackId is { } heard)
        {
            _names[heard] = (latest.Title, latest.Artist);
            if (_names.Count > NamesRemembered) _names.RemoveAt(0);
        }
        foreach (var sample in _ear.Hear(latest, now))
        {
            var sampleId = _store.AddSample(sample);
            if (sample.TrackId != _track)
            {
                // A new track: the listen before it is over, so learn from it.
                // The new listen starts at this sample and waits for its own end.
                _store.Process(beforeId: sampleId);
                if (_track is not null) Name(_track); // it may be kept now, so it may be named
                _track = sample.TrackId;
            }
        }
        if (latest?.TrackId is { } playing) Name(playing);
        _needle.Heard(latest, now);
        _needle.Tick(now);
        if (now - _purgedAtMs >= PurgeEveryMs)
        {
            _store.PurgeSamples(now);
            _purgedAtMs = now;
        }
    }

    /// <summary>Jump to the best part; <paramref name="done"/> hears the decision and, later, the landing.</summary>
    public void Jump(Action<Jumped> done)
    {
        var trackId = _turntable.Read()?.TrackId;
        if (trackId is null)
        {
            done(new Jumped(new JumpResult(Status.PlayerUnavailable), null));
            return;
        }
        var result = Decide(trackId);
        if (result.TargetMs is not { } target)
        {
            done(new Jumped(result, null));
            return;
        }
        _needle.Drop(trackId, target, _clock(), landing => done(new Jumped(result, landing)));
    }

    /// <summary>Pin where the playing track is now (the pin key).</summary>
    public Pinned Pin()
    {
        var latest = _turntable.Read();
        var position = latest is null ? null : PositionNow(latest, _clock());
        if (latest?.TrackId is not { } trackId || position is not { } at) return new Pinned(null);
        return Pinned(trackId, new Pin(at, Typed: false));
    }

    /// <summary>Pin a chosen time on the song on screen: typed as 1:23 (exact), or picked on its line (approximate).</summary>
    public Pinned PinAt(long positionMs, bool exact = true)
    {
        if (Reading?.TrackId is not { } trackId) return new Pinned(null);
        return Pinned(trackId, new Pin(positionMs, Typed: exact));
    }

    /// <summary>Remove the pin of the song on screen; false when it had none.</summary>
    public bool Unpin()
    {
        if (Reading?.TrackId is not { } trackId || _store.Pin(trackId) is null) return false;
        _store.ClearPin(trackId);
        return true;
    }

    /// <summary>What a jump would do now, without moving anything.</summary>
    public JumpResult Decision() =>
        Reading?.TrackId is { } trackId ? Decide(trackId) : new JumpResult(Status.PlayerUnavailable);

    /// <summary>The playing song as a chart for the screen, or null when there is none to draw.</summary>
    public Chart? PlayingChart()
    {
        if (Reading?.TrackId is not { } trackId) return null;
        var seeks = _store.Seeks(trackId);
        return Chart.For(
            Reading, _store.Pin(trackId), Heatmap.Favorite(seeks), seeks, _store.IsIgnored(trackId), _store.LearningPaused());
    }

    // What the listener can change about what is kept. Each acts on the song on
    // screen, the last one the player reported, as the Mac's menu does, and
    // returns false when there is none.

    /// <summary>Forget the playing song: its pin and everything learned from it.</summary>
    public bool ForgetSong() => WithSong(_store.ForgetTrack);

    /// <summary>
    /// Forget what was learned from the playing song, a spot learned wrong or
    /// not the one meant. Its pin stays, and it goes on learning.
    /// </summary>
    public bool ForgetLearnedFromSong() => WithSong(_store.ForgetLearned);

    /// <summary>Stop learning from the playing song and forget what was learned. Its pin stays.</summary>
    public bool IgnoreSong() => WithSong(trackId =>
    {
        _store.Ignore(trackId);
        Name(trackId); // listed as not tracked
    });

    /// <summary>Learn from the playing song again.</summary>
    public bool TrackSongAgain() => WithSong(_store.Unignore);

    public void SetLearningPaused(bool paused) => _store.SetLearningPaused(paused);

    /// <summary>Forget what was learned from every song. Pins, ignored songs and settings stay.</summary>
    public void ForgetAllLearned() => _store.ForgetLearned();

    /// <summary>Delete everything: pins, what was learned, ignored songs and settings.</summary>
    public void ForgetEverything() => _store.ForgetAll();

    /// <summary>Whether the playing track has a spot to jump to.</summary>
    public bool SpotKnown() =>
        Reading?.TrackId is { } trackId &&
        (_store.Pin(trackId) is not null || Heatmap.Favorite(_store.Seeks(trackId)) is not null);

    /// <summary>Everything kept, song by song, for the listener to read.</summary>
    public List<Record> Records() => _store.KeptSongs().Select(kept =>
    {
        var seeks = _store.Seeks(kept.TrackId);
        return new Record(
            kept.Title, kept.Artist, kept.Pin, Heatmap.Favorite(seeks), seeks.Count,
            seeks.Select(s => s.Listen).Distinct().Count(), kept.Ignored);
    }).ToList();

    /// <summary>Where playback is at <paramref name="nowMs"/>: a report may be seconds old while playing goes on.</summary>
    public static long? PositionNow(Reading reading, long nowMs)
    {
        if (reading.PositionMs is not { } position) return null;
        if (!reading.Playing || nowMs <= reading.AtMs) return position;
        var now = position + (nowMs - reading.AtMs);
        return reading.DurationMs is { } duration ? Math.Min(now, duration) : now;
    }

    private JumpResult Decide(string trackId) =>
        Target.Choose(trackLoaded: true, pin: _store.Pin(trackId), favorite: Heatmap.Favorite(_store.Seeks(trackId)));

    private bool WithSong(Action<string> action)
    {
        if (Reading?.TrackId is not { } trackId) return false;
        action(trackId);
        return true;
    }

    private Pinned Pinned(string trackId, Pin pin)
    {
        _store.SetPin(trackId, pin);
        Name(trackId);
        return new Pinned(pin);
    }

    private void Name(string trackId)
    {
        // The store keeps it only if something else is kept about the song.
        if (_names.TryGetValue(trackId, out var name)) _store.Name(trackId, name.Title, name.Artist);
    }
}
