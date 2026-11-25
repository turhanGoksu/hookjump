using System;
using Hookjump.Core;
using Hookjump.Player;

namespace Hookjump.App;

/// <summary>
/// What the strip shows after a key is pressed: the song, its line, where the
/// float goes and where the hook is, and the words. Null times leave the part out.
/// </summary>
/// <param name="FromMs">Where the float starts: where playback was.</param>
/// <param name="ToMs">Where it ends, and where the hook is: the jump's target or the pin.</param>
/// <param name="Moves">The float slides from FromMs to ToMs (a jump that landed).</param>
/// <param name="HookDrops">The hook comes down into the water (a new pin).</param>
public sealed record StripScene(
    string Title,
    long? DurationMs,
    long? FromMs,
    long? ToMs,
    bool Moves,
    bool HookDrops,
    string Words)
{
    /// <summary>A line can be drawn only for a song of known length.</summary>
    public bool HasLine => DurationMs is > 0;
}

/// <summary>One moment of the strip.</summary>
/// <param name="Alpha">How visible the strip is, 0 to 1.</param>
/// <param name="FloatMs">Where the float is on the line, or null for none.</param>
/// <param name="HookMs">Where the hook is, or null for none.</param>
/// <param name="HookDepth">How far the hook has come down, 0 above the water to 1 in place.</param>
/// <param name="Wiggle">A sideways shake of the float, in pixels: nothing moved.</param>
public sealed record StripFrame(double Alpha, double? FloatMs, double? HookMs, double HookDepth, double Wiggle, bool Done);

/// <summary>
/// The strip's timing: it comes in, plays its one move, waits to be read and
/// goes, all in a few seconds. Kept apart from drawing, so it is tested
/// without a screen.
/// </summary>
public static class Strip
{
    public const double Seconds = 2.6;
    private const double FadeIn = 0.15, FadeOut = 0.4;
    private const double MoveStart = 0.1, MoveLength = 0.7;
    private const string NoSong = "hookjump";

    /// <summary>
    /// What a jump shows: the float slides to the hook only when Spotify did
    /// move there; a jump that slipped shows the hook it aimed at, and the
    /// float shaking where playback still is.
    /// </summary>
    /// <param name="before">The song as it was when the key was pressed.</param>
    /// <param name="fromMs">Where playback was then.</param>
    public static StripScene ForJump(Reading? before, long? fromMs, Jumped jumped) => new(
        before?.Title ?? NoSong, before?.DurationMs, fromMs, jumped.Result.TargetMs,
        Moves: jumped.Landing == Landing.Landed, HookDrops: false, Words.DescribeJump(jumped));

    /// <summary>What a pin shows: the hook comes down where the float is; with nothing playing, only words.</summary>
    public static StripScene ForPin(Reading? song, Pin? pinned) => pinned is null
        ? new StripScene(NoSong, null, null, null, Moves: false, HookDrops: false, Words.DescribePin(null))
        : new StripScene(
            song?.Title ?? NoSong, song?.DurationMs, pinned.AtMs, pinned.AtMs, Moves: false, HookDrops: true,
            Words.DescribePin(pinned.AtMs, pinned.Typed));

    public static StripFrame At(StripScene scene, double t)
    {
        if (t >= Seconds) return new StripFrame(0, null, null, 1, 0, true);
        var alpha = Math.Min(1, Math.Min(t / FadeIn, (Seconds - t) / FadeOut));
        var move = EaseOut(Math.Clamp((t - MoveStart) / MoveLength, 0, 1));

        double? floatMs = scene.FromMs;
        if (scene.Moves && scene.FromMs is { } from && scene.ToMs is { } to) floatMs = from + (to - from) * move;

        var depth = scene.HookDrops ? move : 1;
        // Nowhere to go: the float shakes twice, as if to say no.
        var wiggle = !scene.Moves && !scene.HookDrops && t < MoveStart + MoveLength
            ? 3 * Math.Sin((t - MoveStart) / MoveLength * 4 * Math.PI) * (1 - move)
            : 0;
        if (t < MoveStart) wiggle = 0;
        return new StripFrame(alpha, scene.HasLine ? floatMs : null, scene.HasLine ? scene.ToMs : null, depth, wiggle, false);
    }

    private static double EaseOut(double x) => 1 - Math.Pow(1 - x, 3);
}
