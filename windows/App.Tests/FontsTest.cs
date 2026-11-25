using System.IO;
using Xunit;
using static Hookjump.App.Native;

namespace Hookjump.App.Tests;

/// <summary>
/// The phone's typefaces are inside the exe, with their licenses, and GDI+
/// finds them by the names the strip asks for: a wrong name draws no text at
/// all, which only a look at the screen would otherwise show.
/// </summary>
public unsafe class FontsTest
{
    [Theory]
    [InlineData("fonts.barlow_condensed_semibold.ttf")]
    [InlineData("fonts.ibm_plex_mono_regular.ttf")]
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
    public void GdiPlusFindsBothTypefaces()
    {
        var input = new GdiplusStartupInput { Version = 1 };
        Assert.Equal(0, GdiplusStartup(out var token, &input, 0));
        var barlow = Fonts.Barlow(17);
        var plex = Fonts.Plex(11.5f);
        Assert.NotEqual(0, barlow);
        Assert.NotEqual(0, plex);
        GdipDeleteFont(barlow);
        GdipDeleteFont(plex);
        _ = token; // GDI+ stays up: Fonts keeps its families for the process
    }

    private static Stream Resource(string name) =>
        typeof(Fonts).Assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException(name);
}
