using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// The phone's typefaces, Barlow Condensed and IBM Plex Mono (SIL Open Font
/// License), carried inside the exe and loaded once from memory.
/// </summary>
/// <remarks>
/// GDI+ knows a font by its older family name, which carries the weight,
/// except for Bold, which is a style of the plain family.
/// </remarks>
internal static unsafe class Fonts
{
    public const int Bold = 1, Underline = 4;

    public static readonly string[] Files =
    [
        "fonts.barlow_condensed_bold.ttf",
        "fonts.barlow_condensed_semibold.ttf",
        "fonts.barlow_condensed_medium.ttf",
        "fonts.ibm_plex_mono_regular.ttf",
        "fonts.ibm_plex_mono_medium.ttf",
    ];

    private static nint _collection;

    /// <summary>Barlow Condensed Bold, for titles and buttons; delete it with GdipDeleteFont.</summary>
    public static nint BarlowBold(float pixels) => Create("Barlow Condensed", Bold, pixels);

    /// <summary>Barlow Condensed SemiBold, for what the hook is.</summary>
    public static nint Barlow(float pixels, int style = 0) => Create("Barlow Condensed SemiBold", style, pixels);

    /// <summary>Barlow Condensed Medium, for the artist and the details.</summary>
    public static nint BarlowMedium(float pixels) => Create("Barlow Condensed Medium", 0, pixels);

    /// <summary>IBM Plex Mono, for times and small words.</summary>
    public static nint Plex(float pixels) => Create("IBM Plex Mono", 0, pixels);

    /// <summary>IBM Plex Mono Medium, for the times that matter most.</summary>
    public static nint PlexMedium(float pixels) => Create("IBM Plex Mono Medium", 0, pixels);

    private static readonly Dictionary<string, nint> Families = [];

    private static nint Create(string name, int style, float pixels)
    {
        Load();
        // Each family is found once and kept, never deleted: a font may lean
        // on its family for as long as it lives.
        if (!Families.TryGetValue(name, out var family))
        {
            GdipCreateFontFamilyFromName(name, _collection, out family);
            if (family != 0) Families[name] = family;
        }
        GdipCreateFont(family, pixels, style, UnitPixel, out var font);
        return font;
    }

    private static void Load()
    {
        if (_collection != 0) return;
        GdipNewPrivateFontCollection(out _collection);
        foreach (var file in Files)
        {
            using var stream = typeof(Fonts).Assembly.GetManifestResourceStream(file)
                ?? throw new FileNotFoundException("a font is missing from the app", file);
            // GDI+ reads the font from this memory as long as the app runs.
            var memory = NativeMemory.Alloc((nuint)stream.Length);
            stream.ReadExactly(new System.Span<byte>(memory, (int)stream.Length));
            GdipPrivateAddMemoryFont(_collection, memory, (int)stream.Length);
        }
    }
}
