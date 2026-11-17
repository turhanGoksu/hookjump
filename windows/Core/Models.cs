using System;

namespace Hookjump.Core;

/// <summary>
/// Where a jump target came from, or why there is none (spec/ALGORITHM.md, section 2).
/// </summary>
public enum Status
{
    Pinned, // a spot the listener chose by hand
    Personal, // from the listener's own heatmap
    NoData, // nothing pinned or learned yet
    PlayerUnavailable, // Spotify closed or nothing loaded
}

public static class StatusExtensions
{
    /// <summary>
    /// The string used in spec/ JSON vectors; renaming one breaks every vector
    /// and every other implementation.
    /// </summary>
    public static string Value(this Status status) => status switch
    {
        Status.Pinned => "pinned",
        Status.Personal => "personal",
        Status.NoData => "no_data",
        Status.PlayerUnavailable => "player_unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static bool HasTarget(this Status status) =>
        status is Status.Pinned or Status.Personal;
}

/// <summary>What the player reported at one moment, as a platform adapter read it.</summary>
public sealed record PlaybackSample(
    long AtMs, // a clock in ms; only differences matter (stored: Unix time)
    string? TrackId, // null: no track loaded, or the player is unavailable
    bool Playing,
    long? PositionMs, // null: the player did not report a position
    bool OwnSeek = false); // hookjump moved the playhead since the last sample

/// <summary>What seek detection reports: playback carrying on, or the listener moving it.</summary>
public abstract record ListeningEvent(
    string TrackId,
    int Listen, // numbers one play-through of a track, counting from 1
    long FromMs,
    long ToMs);

/// <summary>Uninterrupted playback of one stretch of a track during one listen.</summary>
public sealed record PlaySpan(string TrackId, int Listen, long FromMs, long ToMs)
    : ListeningEvent(TrackId, Listen, FromMs, ToMs);

/// <summary>The listener moved the playhead. Jumps made by hookjump are not seeks.</summary>
/// <param name="FromMs">Where playback was at the sample before the seek.</param>
/// <param name="ToMs">Where it was at the sample after; at most one interval late.</param>
public sealed record SeekEvent(string TrackId, int Listen, long FromMs, long ToMs)
    : ListeningEvent(TrackId, Listen, FromMs, ToMs);

/// <summary>A spot the listener chose by hand for one track.</summary>
public sealed record Pin(
    long AtMs, // the position when the key was pressed, or the typed time
    bool Typed); // true: typed as m:ss, exact; false: a key or a click, approximate

/// <summary>The part of a track the listener's own seeks point to.</summary>
public sealed record Favorite(
    long TargetMs,
    int Points, // rewinds and forward skips that landed in this region
    int Listens); // how many different listens those seeks came from

/// <summary>
/// A jump decision: a target, or an explicit reason why there is none.
/// </summary>
/// <remarks>
/// The two always agree: <c>pinned</c> and <c>personal</c> carry a target,
/// every other status carries none. hookjump never jumps without saying why.
/// </remarks>
public sealed record JumpResult
{
    public JumpResult(Status status, long? targetMs = null)
    {
        if (status.HasTarget() && targetMs is null)
            throw new ArgumentException($"status {status.Value()} needs a target", nameof(targetMs));
        if (!status.HasTarget() && targetMs is not null)
            throw new ArgumentException($"status {status.Value()} must not carry a target", nameof(targetMs));
        if (targetMs < 0)
            throw new ArgumentException($"target must be >= 0, got {targetMs}", nameof(targetMs));
        Status = status;
        TargetMs = targetMs;
    }

    public Status Status { get; }

    public long? TargetMs { get; }
}
