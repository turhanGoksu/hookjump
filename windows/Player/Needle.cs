using System;
using Hookjump.Core;

namespace Hookjump.Player;

/// <summary>Where a jump ended up.</summary>
public enum Landing
{
    Landed, // the playhead is where it was sent
    TrackChanged, // another track is playing now; no move
    Slipped, // the player took the move but did not go there, twice
    NoPlayer, // the player is not running
}

/// <summary>Jumps on a turntable and checks every jump.</summary>
/// <remarks>
/// The player confirms a move later, in a report, so a jump waits: each report
/// goes to <see cref="Heard"/>, and <see cref="Tick"/> gives up on a silent
/// player after <see cref="SettleMs"/>. A move that does not land is tried once
/// more, then reported.
/// </remarks>
public sealed class Needle(ITurntable turntable, Ear ear)
{
    public const int Retries = 1; // one more try before reporting a slip
    public const long SettleMs = 2000; // how long a player may take to confirm a move

    private sealed class Jump(string trackId, long targetMs, Action<Landing> done)
    {
        public string TrackId { get; } = trackId;
        public long TargetMs { get; } = targetMs;
        public Action<Landing> Done { get; } = done;
        public int Tries { get; set; }
        public long PlacedAtMs { get; set; }
    }

    private Jump? _jump;

    /// <summary>Start a jump; <paramref name="done"/> hears where it ended, now or after later reports.</summary>
    public void Drop(string trackId, long positionMs, long nowMs, Action<Landing> done)
    {
        _jump?.Done(Landing.Slipped); // a new press replaces a jump still waiting
        _jump = new Jump(trackId, positionMs, done);
        Attempt(nowMs);
    }

    /// <summary>A new report from the player.</summary>
    public void Heard(Reading? reading, long nowMs)
    {
        if (_jump is not { } current) return;
        if (reading is null) Finish(Landing.NoPlayer);
        else if (reading.TrackId != current.TrackId) Finish(Landing.TrackChanged);
        else if (reading.AtMs < current.PlacedAtMs) { } // said before the move: not an answer
        else if (Near(reading.PositionMs, current.TargetMs)) Finish(Landing.Landed);
        else RetryOrSlip(nowMs);
    }

    /// <summary>Call now and then; a move the player never confirms counts as slipped.</summary>
    public void Tick(long nowMs)
    {
        if (_jump is { } current && nowMs - current.PlacedAtMs >= SettleMs) RetryOrSlip(nowMs);
    }

    private void Attempt(long nowMs)
    {
        if (_jump is not { } current) return;
        current.Tries += 1;
        current.PlacedAtMs = nowMs;
        if (turntable.Place(current.TrackId, current.TargetMs))
        {
            ear.HeardOwnMove(); // even a slipped move may have moved the playhead
            return;
        }
        var reading = turntable.Read();
        if (reading is null) Finish(Landing.NoPlayer);
        else if (reading.TrackId != current.TrackId) Finish(Landing.TrackChanged);
        else RetryOrSlip(nowMs);
    }

    private void RetryOrSlip(long nowMs)
    {
        if (_jump is not { } current) return;
        if (current.Tries <= Retries) Attempt(nowMs);
        else Finish(Landing.Slipped);
    }

    private void Finish(Landing landing)
    {
        if (_jump is not { } current) return;
        _jump = null;
        current.Done(landing);
    }

    private static bool Near(long? positionMs, long targetMs) =>
        positionMs is { } position && Math.Abs(position - targetMs) <= Seek.ToleranceMs;
}
