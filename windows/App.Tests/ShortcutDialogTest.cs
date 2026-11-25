using Xunit;
using static Hookjump.App.Native;

namespace Hookjump.App.Tests;

/// <summary>"New shortcut to jump": the dialog, opened hidden and answered as Windows would.</summary>
public unsafe class ShortcutDialogTest
{
    private static readonly Shortcut CtrlAltJ = new(Modifiers.Ctrl | Modifiers.Alt, 'J');

    [Theory]
    [InlineData(0x0000, null)]
    [InlineData(0x064A, "Ctrl+Alt+J")] // HOTKEYF_CONTROL | HOTKEYF_ALT, 'J'
    [InlineData(0x0137, "Shift+7")]
    [InlineData(0x0746, "Ctrl+Alt+Shift+F")]
    public void TheBoxsKeysAreReadInItsOwnOrder(int value, string? keys) =>
        Assert.Equal(keys, ShortcutDialog.FromBox(value)?.ToString());

    [Fact]
    public void ItSaysWhatTheKeysAreNowAsTheMacDoes()
    {
        Assert.Equal(
            "Now it is Ctrl+Alt+F. Combinations that would stop you typing a character, or that every app uses, are not accepted.",
            ShortcutDialog.Says(Shortcuts.Jump));
        Assert.StartsWith("Now there is none.", ShortcutDialog.Says(null));
    }

    [Fact]
    public void SaveWaitsForKeysThatCanBeTaken()
    {
        var dialog = ShortcutDialog.Open("jump", Shortcuts.Jump, keys => keys == CtrlAltJ ? null : $"{keys} is no good.");
        try
        {
            Assert.NotEqual(0, dialog);
            Assert.Equal("New shortcut to jump", Text(dialog));
            Assert.Equal("Save", Text(GetDlgItem(dialog, IdOk)));
            Assert.False(IsWindowEnabled(GetDlgItem(dialog, IdOk))); // nothing pressed yet

            Press(dialog, 0x0644); // Ctrl+Alt+D
            Assert.Equal("Ctrl+Alt+D is no good.", Text(GetDlgItem(dialog, ShortcutDialog.Problem)));
            Assert.False(IsWindowEnabled(GetDlgItem(dialog, IdOk)));

            Press(dialog, 0x064A); // Ctrl+Alt+J
            Assert.Equal("", Text(GetDlgItem(dialog, ShortcutDialog.Problem)));
            Assert.True(IsWindowEnabled(GetDlgItem(dialog, IdOk)));
            SendMessage(dialog, WmCommand, IdOk, 0);
            Assert.Equal(CtrlAltJ, ShortcutDialog.Result);
        }
        finally
        {
            DestroyWindow(dialog);
        }
    }

    [Fact]
    public void CancelKeepsTheKeys()
    {
        var dialog = ShortcutDialog.Open("pin", Shortcuts.Pin, _ => null);
        try
        {
            Press(dialog, 0x064A);
            SendMessage(dialog, WmCommand, IdCancel, 0);
            Assert.Null(ShortcutDialog.Result);
        }
        finally
        {
            DestroyWindow(dialog);
        }
    }

    /// <summary>Puts keys in the box, and tells the dialog, as the box does when keys are pressed.</summary>
    private static void Press(nint dialog, int keys)
    {
        var box = GetDlgItem(dialog, ShortcutDialog.Box);
        SendMessage(box, HkmSetHotkey, keys, 0);
        SendMessage(dialog, WmCommand, ((nint)EnChange << 16) | ShortcutDialog.Box, box);
    }

    private static string Text(nint window)
    {
        var text = stackalloc char[256];
        return new string(text, 0, GetWindowText(window, text, 256));
    }
}
