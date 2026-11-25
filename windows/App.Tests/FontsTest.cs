using System;
using System.IO;
using System.Linq;
using Xunit;
using static Hookjump.App.Native;

namespace Hookjump.App.Tests;

/// <summary>
/// The phone's typefaces are inside the exe, with their licenses, and GDI+
/// finds every weight by the name the strip and the panel ask for: a wrong
/// name draws no text at all, which only a look at the screen would otherwise
/// show.
/// </summary>
public unsafe class FontsTest
{
    [Theory]
    [InlineData("fonts.barlow_condensed_bold.ttf")]
    [InlineData("fonts.barlow_condensed_semibold.ttf")]
    [InlineData("fonts.barlow_condensed_medium.ttf")]
    [InlineData("fonts.ibm_plex_mono_regular.ttf")]
    [InlineData("fonts.ibm_plex_mono_medium.ttf")]
    public void EachTypefaceIsInsideTheExe(string name)
    {
        using var font = Resource(name);
        var head = new byte[4];
        font.ReadExactly(head);
        Assert.Equal(new byte[] { 0, 1, 0, 0 }, head); // a TrueType font
    }

    [Theory]
    [InlineData("licenses.BarlowCondensed-OFL.txt")]
    [InlineData("licenses.IBMPlexMono-OFL.txt")]
    public void EachTypefaceCarriesItsLicense(string name)
    {
        using var license = new StreamReader(Resource(name));
        Assert.Contains("SIL OPEN FONT LICENSE", license.ReadToEnd());
    }

    [Fact]
    public void TheExeCarriesExactlyTheTypefacesFontsLoads()
    {
        var inside = typeof(Fonts).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("fonts.", StringComparison.Ordinal));
        Assert.Equal(Fonts.Files.Order(), inside.Order());
    }

    [Fact]
    public void GdiPlusFindsEveryWeightItself()
    {
        // GDI+ makes up a weight it cannot find (a bold from the regular), so
        // a font is not enough: it must come from the right face.
        var input = new GdiplusStartupInput { Version = 1 };
        Assert.Equal(0, GdiplusStartup(out var token, &input, 0));
        (nint Font, string Family, int Style)[] fonts =
        [
            (Fonts.BarlowBold(26), "Barlow Condensed", Fonts.Bold),
            (Fonts.Barlow(17), "Barlow Condensed SemiBold", 0),
            (Fonts.Barlow(14, Fonts.Underline), "Barlow Condensed SemiBold", Fonts.Underline),
            (Fonts.BarlowMedium(15), "Barlow Condensed Medium", 0),
            (Fonts.Plex(11), "IBM Plex Mono", 0),
            (Fonts.PlexMedium(12), "IBM Plex Mono Medium", 0),
        ];
        foreach (var (font, family, style) in fonts)
        {
            Assert.NotEqual(0, font);
            Assert.Equal((family, style), (Family(font), Style(font)));
            GdipDeleteFont(font);
        }
        _ = token; // GDI+ stays up: Fonts keeps its collection for the process
    }

    private static string Family(nint font)
    {
        // The font's own family: it goes with the font, so it is not deleted here.
        GdipGetFamily(font, out var family);
        var name = stackalloc char[32]; // LF_FACESIZE
        GdipGetFamilyName(family, name, 0);
        return new string(name);
    }

    private static int Style(nint font)
    {
        GdipGetFontStyle(font, out var style);
        return style;
    }

    private static Stream Resource(string name) =>
        typeof(Fonts).Assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException(name);
}
