namespace Hookjump.App;

/// <summary>The phone's colors (android FishingLineScreen.kt), as GDI+ ARGB values.</summary>
internal static class Sea
{
    public const uint Water = 0xFFDCEAE6; // the sea
    public const uint Ink = 0xFF12304A; // the line, the words
    public const uint Buoy = 0xFFB5301F; // the float, the hook
    public const uint Contour = 0xFFB4D0CA; // the depth lines
    public const uint Kelp = 0xFF2F7D73; // where the listener skipped to
    public const uint Foam = 0xFFF4F8F6; // the float's band

    /// <summary>The same color, this visible (0 to 1).</summary>
    public static uint Faded(uint argb, double alpha) => ((uint)(alpha * 255) << 24) | (argb & 0xFFFFFF);
}
