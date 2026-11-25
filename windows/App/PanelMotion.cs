using System;

namespace Hookjump.App;

/// <summary>What the hook says on a chart; a change in it may be a cast or a reel.</summary>
public sealed record HookWords(string Title, long? Ms, bool Pinned, string Label, string Detail)
{
    public static HookWords Of(Chart chart) => new(chart.Title, chart.HookMs, chart.Pinned, chart.HookLabel, chart.HookDetail);
}

/// <summary>Where a cast or a reel is at one moment. Fractions run from 0 to 1; null leaves the part out.</summary>
/// <param name="Flight">The hook in the air: 0 far up to the right, 1 at the surface.</param>
/// <param name="Depth">The hook in the water: 0 at the surface, 1 in its place on the line.</param>
/// <param name="Splash">The ring where the hook meets the surface, spreading.</param>
/// <param name="Words">How visible the new words are.</param>
/// <param name="OldWords">How visible the words from before are.</param>
public sealed record HookFrame(double? Flight, double? Depth, double? Splash, double Words, double OldWords);

/// <summary>
/// A new pin is a cast: the hook flies in over the water, splashes in at the
/// surface, and sinks to its second. A removed pin is reeled in: the hook
/// rises to the surface and flies off. The phone's timings, so both move alike.
/// </summary>
public sealed record HookChange(bool Cast, HookWords Before)
{
    public const double CastSeconds = 1.3, ReelSeconds = 1.2, RippleSeconds = 1.1;
    private const double FlightPart = 0.27, SplashPart = 0.12, RisePart = 0.55;

    public double Seconds => Cast ? CastSeconds : ReelSeconds;

    /// <summary>
    /// The change from one chart's hook to the next, or null when nothing
    /// moves: no motion wanted, another song, or a hook that is not a pin.
    /// </summary>
    public static HookChange? Of(HookWords? before, HookWords after, bool motion)
    {
        if (!motion || before is null || before.Title != after.Title) return null;
        if (after.Pinned && after.Ms is not null && after.Ms != before.Ms) return new(true, before);
        if (before.Pinned && !after.Pinned && before.Ms is not null) return new(false, before);
        return null;
    }

    /// <summary>The cast or the reel <paramref name="seconds"/> after it began.</summary>
    public HookFrame At(double seconds)
    {
        var p = Math.Clamp(seconds / Seconds, 0, 1);
        // The old words fade out as it starts; the new ones come once it is done.
        var words = Math.Clamp((p - 0.85) / 0.15, 0, 1);
        var old = p < 0.15 ? 1 - p / 0.15 : 0;
        if (Cast)
        {
            if (p < FlightPart) return new(p / FlightPart, null, null, words, old);
            var t = (p - FlightPart) / (1 - FlightPart);
            var splash = p < FlightPart + SplashPart ? (p - FlightPart) / SplashPart : (double?)null;
            return new(null, 1 - Math.Pow(1 - t, 3), splash, words, old);
        }
        if (p < RisePart) return new(null, 1 - Math.Pow(p / RisePart, 3), null, words, old);
        var up = p < RisePart + SplashPart ? (p - RisePart) / SplashPart : (double?)null;
        return new(1 - (p - RisePart) / (1 - RisePart), null, up, words, old);
    }
}

/// <summary>
/// The sea's own clock: it runs only while the song plays and motion is
/// wanted, so a paused song stops the water where it is, and draws nothing.
/// </summary>
public sealed class SeaClock
{
    public long Ms { get; private set; }

    public void Advance(long elapsedMs, bool playing, bool motion)
    {
        if (playing && motion && elapsedMs > 0) Ms += elapsedMs;
    }

    /// <summary>Where the clock is in a cycle of this length, from 0 to 1.</summary>
    public double Cycle(long periodMs) => (double)(Ms % periodMs) / periodMs;
}

/// <summary>The sea's slow motions, from its clock, in pixels and degrees.</summary>
public static class Sway
{
    /// <summary>How far the current bends the line at a point along it: none at its two ends, most in the middle.</summary>
    public static float Line(SeaClock sea, float x)
    {
        var along = Math.Clamp((x - Panel.LineLeft) / (Panel.LineRight - Panel.LineLeft), 0, 1);
        return (float)(Math.Sin(2 * Math.PI * sea.Cycle(11_000)) * 5 * Math.Sin(Math.PI * along));
    }

    /// <summary>The float bobs up and down while the song plays.</summary>
    public static float Bob(SeaClock sea) => (float)(Math.Sin(2 * Math.PI * sea.Cycle(2_600)) * 1.6);

    /// <summary>And tilts a little, just after each bob.</summary>
    public static float Tilt(SeaClock sea) => (float)(Math.Sin(2 * Math.PI * sea.Cycle(2_600) + 0.6) * 4);
}

/// <summary>
/// After a seek or a jump, the float glides to its new place instead of
/// leaping; along normal playback it simply follows. A soft spring, as on the
/// phone: it settles in under a second, overshooting a little.
/// </summary>
public static class Glide
{
    private const double Stiffness = 50, Damping = 0.85; // the phone's very low stiffness, 0.85 damping
    private static readonly double Natural = Math.Sqrt(Stiffness);
    private static readonly double Damped = Natural * Math.Sqrt(1 - Damping * Damping);

    /// <summary>How far, in pixels, a float that started <paramref name="from"/> its place still is after this time.</summary>
    public static double Offset(double from, double seconds)
    {
        if (seconds <= 0) return from;
        var decay = Math.Exp(-Damping * Natural * seconds);
        return from * decay * (Math.Cos(Damped * seconds) + Damping * Natural / Damped * Math.Sin(Damped * seconds));
    }

    /// <summary>Settled: less than a third of a pixel to go, and no more moving to see.</summary>
    public static bool Settled(double from, double seconds) =>
        Math.Abs(from) * Math.Exp(-Damping * Natural * seconds) * (1 + Damping * Natural / Damped) < 0.3;

    /// <summary>A move between two frames this large (in pixels) is a seek, not playback going on.</summary>
    public const float Leap = 4;
}
