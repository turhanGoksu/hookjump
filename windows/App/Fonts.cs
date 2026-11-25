using System.IO;
using System.Runtime.InteropServices;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// The phone's typefaces, Barlow Condensed and IBM Plex Mono (SIL Open Font
/// License), carried inside the exe and loaded once from memory.
/// </summary>
internal static unsafe class Fonts
{
    private static nint _barlow, _plex;

    /// <summary>A Barlow Condensed SemiBold font of this pixel size; delete it with GdipDeleteFont.</summary>
    public static nint Barlow(float pixels) => Create(ref _barlow, "Barlow Condensed SemiBold", pixels);

    /// <summary>An IBM Plex Mono font of this pixel size; delete it with GdipDeleteFont.</summary>
    public static nint Plex(float pixels) => Create(ref _plex, "IBM Plex Mono", pixels);

    private static nint Create(ref nint family, string name, float pixels)
    {
        if (family == 0) family = Load(name);
        GdipCreateFont(family, pixels, 0, UnitPixel, out var font);
        return font;
    }

    private static nint _collection;

    // GDI+ knows a font by its older family name, which carries the weight.
    private static nint Load(string name)
    {
        if (_collection == 0)
        {
            GdipNewPrivateFontCollection(out _collection);
            foreach (var file in new[] { "fonts.barlow_condensed_semibold.ttf", "fonts.ibm_plex_mono_regular.ttf" })
            {
                using var stream = typeof(Fonts).Assembly.GetManifestResourceStream(file)
                    ?? throw new FileNotFoundException("a font is missing from the app", file);
                // GDI+ reads the font from this memory as long as the app runs.
                var memory = NativeMemory.Alloc((nuint)stream.Length);
                stream.ReadExactly(new System.Span<byte>(memory, (int)stream.Length));
                GdipPrivateAddMemoryFont(_collection, memory, (int)stream.Length);
            }
        }
        GdipCreateFontFamilyFromName(name, _collection, out var family);
        return family;
    }
}
