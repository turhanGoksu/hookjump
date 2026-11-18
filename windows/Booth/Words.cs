using System;
using System.Collections.Generic;
using System.Globalization;
using Hookjump.Core;
using Hookjump.Player;

namespace Hookjump;

/// <summary>What hookjump says, in words: the same sentences as src/hookjump/words.py and android/booth.</summary>
public static class Words
{
    public const string NoSong = "Nothing done: Spotify is not playing anything.";
    public const string ForgotEverything = "Deleted everything hookjump kept.";

    private static string Reason(Status status) => status switch
    {
        Status.Pinned => "your pinned spot",
        Status.Personal => "the part you replay most",
        Status.NoData => "nothing pinned or learned for this song yet",
        Status.PlayerUnavailable => "Spotify is not playing anything",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static string Why(Landing landing) => landing switch
    {
        Landing.Slipped => "Spotify did not move there",
        Landing.TrackChanged => "the song changed meanwhile",
        Landing.NoPlayer => "Spotify closed meanwhile",
        _ => throw new ArgumentOutOfRangeException(nameof(landing), landing, null),
    };

    /// <summary>1:43 for 103500; seconds are cut, not rounded, like players show them.</summary>
    public static string Clock(long ms) =>
        string.Create(CultureInfo.InvariantCulture, $"{ms / 60_000}:{ms % 60_000 / 1000:00}");

    public static string DescribeJump(Jumped jumped)
    {
        var reason = Reason(jumped.Result.Status);
        if (jumped.Result.TargetMs is not { } target) return $"No jump: {reason}.";
        if (jumped.Landing is { } landing && landing != Landing.Landed)
            return $"Tried {Clock(target)} ({reason}), but {Why(landing)}.";
        return $"Jumped to {Clock(target)}: {reason}.";
    }

    /// <summary>What a jump would do now, before any move.</summary>
    public static string DescribeTarget(JumpResult result)
    {
        var reason = Reason(result.Status);
        if (result.TargetMs is { } target) return $"Jumps to {Clock(target)}: {reason}.";
        return result.Status == Status.PlayerUnavailable ? $"{reason}." : $"No jump: {reason}.";
    }

    public static string DescribePin(long? pinnedMs, bool exact = false) => pinnedMs switch
    {
        null => "Nothing pinned: Spotify is not playing anything.",
        { } ms when exact => $"Pinned {Clock(ms)}. The jump lands right there.",
        { } ms => $"Pinned {Clock(ms)}. The jump lands 1.5 s before it.",
    };

    public static string DescribeUnpin(bool removed) =>
        removed ? "Pin removed." : "Nothing to remove: this song has no pin.";

    // What the listener changed. A false means no song was playing to act on.

    public static string DescribeForget(bool done) => done ? "Forgot this song: its pin and what was learned." : NoSong;

    public static string DescribeIgnore(bool done) => done ? "Not tracking this song. Its pin stays." : NoSong;

    public static string DescribeTrackAgain(bool done) => done ? "Tracking this song again." : NoSong;

    public static string DescribePause(bool paused) => paused ? "Learning paused. Pins still work." : "Learning again.";

    /// <summary>Why a typed time cannot be pinned, or null when it can.</summary>
    public static string? TypedTimeProblem(long? ms, long durationMs) => ms switch
    {
        null => "Type a time like 1:23.",
        { } at when at > durationMs => $"This song is {Clock(durationMs)} long.",
        _ => null,
    };

    // "What hookjump keeps": one song's entry.

    public static string RecordTitle(Record record) => record.Title ?? "A song not played since names were kept";

    /// <summary>What is kept about one song, one fact per line.</summary>
    public static List<string> DescribeRecord(Record record)
    {
        var lines = new List<string>();
        if (record.Pin is { } pin) lines.Add(pin.Typed ? $"Pinned at {Clock(pin.AtMs)}, typed" : $"Pinned at {Clock(pin.AtMs)}");
        if (record.Favorite is { } favorite)
            lines.Add($"Learned {Clock(favorite.TargetMs)}, from {Count(favorite.Listens, "listen")}");
        else if (record.Seeks > 0)
            lines.Add($"{Count(record.Seeks, "seek")} in {Count(record.Listens, "listen")}, nothing learned yet");
        if (record.Ignored) lines.Add("Not tracking");
        return lines;
    }

    /// <summary><c>1:23</c>, <c>1:23.5</c> or <c>83</c> to milliseconds; null if it is not a time.</summary>
    public static long? ParseClock(string text)
    {
        var trimmed = text.Trim();
        var colon = trimmed.LastIndexOf(':');
        var minutesText = colon >= 0 ? trimmed[..colon] : "";
        var whole = 0;
        if (minutesText.Length > 0 && !int.TryParse(minutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out whole))
            return null;
        if (!double.TryParse(trimmed[(colon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var part))
            return null;
        if (whole < 0 || part < 0 || !double.IsFinite(part) || (minutesText.Length > 0 && part >= 60)) return null;
        // Half a millisecond rounds up, as Kotlin's Math.round does (C#'s Math.Round would round to even).
        return whole * 60_000L + (long)Math.Floor(part * 1000 + 0.5);
    }

    private static string Count(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
}
