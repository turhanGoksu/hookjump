using Hookjump.Player;
using Xunit;
using static Hookjump.App.Native;

namespace Hookjump.App.Tests;

/// <summary>"Pin at a time": what a typed time comes to, and the dialog that asks for it.</summary>
public unsafe class TypedPinTest
{
    private static readonly Reading Sorma = new(0, "smtc:1", "Sorma", "Pinhani", 279267, true, 140000);
    private const uint EmGetCueBanner = 0x1502, EmGetLimitText = 0x00D5;

    [Theory]
    [InlineData("1:23", 83000)]
    [InlineData(" 1:23.5 ", 83500)]
    [InlineData("83", 83000)]
    [InlineData("4:39", 279000)] // within the song's last second
    public void ATimeWithinTheSongIsPinnedExactly(string typed, long ms) =>
        Assert.Equal(new TypedPin(ms, null), TypedPin.Check(typed, "smtc:1", Sorma));

    [Theory]
    [InlineData("", "Type a time like 1:23.")]
    [InlineData("abc", "Type a time like 1:23.")]
    [InlineData("1:75", "Type a time like 1:23.")]
    [InlineData("4:40", "This song is 4:39 long.")]
    public void ATimeThatCannotBePinnedSaysWhy(string typed, string problem) =>
        Assert.Equal(new TypedPin(null, problem), TypedPin.Check(typed, "smtc:1", Sorma));

    [Fact]
    public void ATimeForOneSongIsNeverPinnedOnAnother()
    {
        var changed = new TypedPin(null, "The song changed meanwhile, so nothing was pinned.");
        Assert.Equal(changed, TypedPin.Check("1:23", "smtc:1", Sorma with { TrackId = "smtc:2" }));
        Assert.Equal(changed, TypedPin.Check("1:23", "smtc:1", null)); // Spotify closed
    }

    [Fact]
    public void ASongOfUnknownLengthTakesAnyTime() =>
        Assert.Equal(new TypedPin(99 * 60_000, null), TypedPin.Check("99:00", "smtc:1", Sorma with { DurationMs = null }));

    // The dialog, opened hidden and answered as Windows would -----------------

    [Fact]
    public void TheDialogAsksAsTheMacDoes()
    {
        var dialog = PinDialog.Open(_ => new TypedPin(null, null));
        try
        {
            Assert.NotEqual(0, dialog);
            Assert.Equal("Pin at a time", Text(dialog));
            Assert.Equal("Pin", Text(GetDlgItem(dialog, IdOk)));
            Assert.Equal("Cancel", Text(GetDlgItem(dialog, IdCancel)));
            var box = GetDlgItem(dialog, PinDialog.Box);
            var cue = stackalloc char[16];
            Assert.NotEqual(0, SendMessage(box, EmGetCueBanner, (nint)cue, 16));
            Assert.Equal("1:23", new string(cue)); // the box shows the form of a time
            Assert.Equal(16, SendMessage(box, EmGetLimitText, 0, 0));
            Assert.Equal("", Text(GetDlgItem(dialog, PinDialog.Problem)));
        }
        finally
        {
            DestroyWindow(dialog);
        }
    }

    [Fact]
    public void AProblemIsSaidInTheDialogAndAGoodTimeIsTheAnswer()
    {
        var dialog = PinDialog.Open(typed => TypedPin.Check(typed, "smtc:1", Sorma));
        try
        {
            SetDlgItemText(dialog, PinDialog.Box, "4:40");
            SendMessage(dialog, WmCommand, IdOk, 0);
            Assert.Equal("This song is 4:39 long.", Text(GetDlgItem(dialog, PinDialog.Problem)));
            Assert.Null(PinDialog.Result);

            SetDlgItemText(dialog, PinDialog.Box, "1:23");
            SendMessage(dialog, WmCommand, IdOk, 0);
            Assert.Equal(83000, PinDialog.Result);
        }
        finally
        {
            DestroyWindow(dialog);
        }
    }

    [Fact]
    public void CancelPinsNothing()
    {
        var dialog = PinDialog.Open(typed => TypedPin.Check(typed, "smtc:1", Sorma));
        try
        {
            SetDlgItemText(dialog, PinDialog.Box, "1:23");
            SendMessage(dialog, WmCommand, IdCancel, 0);
            Assert.Null(PinDialog.Result);
        }
        finally
        {
            DestroyWindow(dialog);
        }
    }

    private static string Text(nint window)
    {
        var text = stackalloc char[128];
        return new string(text, 0, GetWindowText(window, text, 128));
    }
}
