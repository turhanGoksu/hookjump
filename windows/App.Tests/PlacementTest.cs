using Xunit;

namespace Hookjump.App.Tests;

/// <summary>Where the strip and the panel go on a screen, in that screen's own pixels.</summary>
public class PlacementTest
{
    [Theory]
    [InlineData(96u, 460)] // 100 %
    [InlineData(120u, 575)] // 125 %, this PC
    [InlineData(144u, 690)] // 150 %
    [InlineData(192u, 920)] // 200 %
    public void SizesGrowWithTheScreensScale(uint dpi, int pixels) => Assert.Equal(pixels, Placement.Pixels(460, dpi));

    [Fact]
    public void APartPixelIsAWholeOne() => Assert.Equal(134, Placement.Pixels(107, 120)); // 107 × 1.25 = 133.75

    [Theory]
    [InlineData(96u, 12)]
    [InlineData(120u, 15)]
    [InlineData(144u, 18)]
    public void TheGapFromTheEdgesGrowsToo(uint dpi, int margin) => Assert.Equal(margin, Placement.Margin(dpi));

    [Fact]
    public void ItSitsByTheClockOfThatScreen()
    {
        // This PC: 1920 × 1080 at 125 %, the taskbar taking the bottom 60.
        Assert.Equal((1920 - 575 - 15, 1020 - 498 - 15), Placement.Corner(1920, 1020, 575, 498, 120));
        // A second screen to the right, at 100 %, its work area ending at 3840 × 1040.
        Assert.Equal((3840 - 320 - 12, 1040 - 108 - 12), Placement.Corner(3840, 1040, 320, 108, 96));
    }
}
