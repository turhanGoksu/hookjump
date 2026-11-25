using System.Collections.Generic;
using Xunit;

namespace Hookjump.App.Tests;

/// <summary>Which hotkeys are used, and what is said when the first choice cannot be.</summary>
public class ShortcutsTest
{
    private readonly HashSet<string> _taken = [];
    private readonly HashSet<string> _typing = [];
    private readonly Dictionary<int, Shortcut> _held = [];

    private ShortcutChoice Choose() => Shortcuts.Choose(
        key => _typing.Contains(key.ToString()),
        (id, key) =>
        {
            if (_taken.Contains(key.ToString())) return false;
            _held[id] = key;
            return true;
        },
        id => _held.Remove(id));

    [Fact]
    public void ShortcutsAreWrittenTheWindowsWay()
    {
        Assert.Equal("Ctrl+Alt+F", Shortcuts.Jump.ToString());
        Assert.Equal("Ctrl+Alt+Shift+D", Shortcuts.PinFallback.ToString());
        Assert.Equal("Win+Ctrl+Alt+Shift+K", new Shortcut(Modifiers.Win | Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift, 'K').ToString());
    }

    [Fact]
    public void TheMacLettersWithCtrlAltComeFirst()
    {
        Assert.Equal(new ShortcutChoice(Shortcuts.Jump, Shortcuts.Pin, null), Choose());
        Assert.Equal(new Dictionary<int, Shortcut> { [Shortcuts.JumpId] = Shortcuts.Jump, [Shortcuts.PinId] = Shortcuts.Pin }, _held);
    }

    [Fact]
    public void AKeyThatTypesACharacterIsNeverTaken()
    {
        _typing.Add("Ctrl+Alt+F"); // AltGr+F types "[" on a Hungarian keyboard
        var choice = Choose();
        Assert.Equal((Shortcuts.JumpFallback, Shortcuts.PinFallback), (choice.Jump, choice.Pin));
        Assert.Equal(
            "Ctrl+Alt+F types a character on your keyboard, so Ctrl+Alt+Shift+F and Ctrl+Alt+Shift+D are used.",
            choice.Note);
    }

    [Fact]
    public void AHalfTakenPairIsLetGoBeforeTheFallback()
    {
        _taken.Add("Ctrl+Alt+D");
        var choice = Choose();
        Assert.Equal("Another app has Ctrl+Alt+D, so Ctrl+Alt+Shift+F and Ctrl+Alt+Shift+D are used.", choice.Note);
        Assert.Equal([Shortcuts.JumpFallback, Shortcuts.PinFallback], _held.Values); // Ctrl+Alt+F was let go
    }

    [Fact]
    public void WithNoPairFreeTheMenuSaysSo()
    {
        _taken.Add("Ctrl+Alt+F");
        _taken.Add("Ctrl+Alt+Shift+F");
        var choice = Choose();
        Assert.Equal((null, null), (choice.Jump, choice.Pin));
        Assert.Equal(
            "No shortcuts: another app has Ctrl+Alt+F, and another app has Ctrl+Alt+Shift+F. Jump and Pin are in this menu.",
            choice.Note);
        Assert.Empty(_held);
    }

    // Keys the listener chose -------------------------------------------------

    private static readonly Shortcut CtrlAltJ = new(Modifiers.Ctrl | Modifiers.Alt, 'J');
    private static readonly Shortcut CtrlShiftK = new(Modifiers.Ctrl | Modifiers.Shift, 'K');

    private ShortcutChoice Choose(Shortcut? jump, Shortcut? pin) => Shortcuts.Choose(
        jump, pin,
        key => _typing.Contains(key.ToString()),
        (id, key) =>
        {
            if (_taken.Contains(key.ToString())) return false;
            _held[id] = key;
            return true;
        },
        id => _held.Remove(id));

    [Fact]
    public void KeptKeysComeBackAsTheyWere()
    {
        Assert.Equal(CtrlAltJ, Shortcut.FromSetting(CtrlAltJ.ToSetting()));
        Assert.Equal(('J' << 16) | 0x3, CtrlAltJ.ToSetting()); // the virtual-key code above RegisterHotKey's mask
        Assert.Null(Shortcut.FromSetting(0)); // the default
        Assert.Null(Shortcut.FromSetting('J' << 16)); // no modifiers
        Assert.Null(Shortcut.FromSetting((0x70 << 16) | 0x3)); // F1: not a letter or a digit
        Assert.Null(Shortcut.FromSetting(('J' << 16) | 0x40)); // a modifier Windows does not have
    }

    [Fact]
    public void ChosenKeysAreTakenAndTheOtherStaysTheDefault()
    {
        Assert.Equal(new ShortcutChoice(CtrlAltJ, Shortcuts.Pin, null), Choose(CtrlAltJ, null));
        Assert.Equal(CtrlAltJ, _held[Shortcuts.JumpId]);
        _held.Clear();
        Assert.Equal(new ShortcutChoice(Shortcuts.Jump, CtrlShiftK, null), Choose(null, CtrlShiftK)); // only the pin changed
        Assert.Equal(CtrlShiftK, _held[Shortcuts.PinId]);
    }

    [Fact]
    public void AChosenKeyTakenByAnotherAppIsNeverSwappedSilently()
    {
        _taken.Add("Ctrl+Alt+J");
        var choice = Choose(CtrlAltJ, CtrlShiftK);
        Assert.Equal(new ShortcutChoice(null, CtrlShiftK, "Another app has Ctrl+Alt+J: change it in Shortcuts."), choice);
        Assert.DoesNotContain(Shortcuts.JumpFallback, _held.Values); // no fallback for a chosen key
    }

    [Fact]
    public void AChosenKeyThatTypesNowIsLetGoAndSaid()
    {
        _typing.Add("Ctrl+Alt+J"); // a keyboard added since
        _taken.Add("Ctrl+Shift+K");
        Assert.Equal(
            new ShortcutChoice(null, null,
                "Ctrl+Alt+J now types a character on your keyboard, and another app has Ctrl+Shift+K: change them in Shortcuts."),
            Choose(CtrlAltJ, CtrlShiftK));
        Assert.Empty(_held);
    }

    [Theory]
    [InlineData(Modifiers.Ctrl | Modifiers.Alt, 'J', null)]
    [InlineData(Modifiers.Ctrl | Modifiers.Shift, 'K', null)]
    [InlineData(Modifiers.Alt | Modifiers.Shift, '7', null)]
    [InlineData(Modifiers.Ctrl | Modifiers.Alt, 'p', "Use a letter or a digit, with Ctrl or Alt.")] // F1, as the box gives it
    [InlineData(Modifiers.Shift, 'J', "Shift+J alone is typing: hold Ctrl or Alt with it.")]
    [InlineData(Modifiers.None, 'J', "J alone is typing: hold Ctrl or Alt with it.")]
    [InlineData(Modifiers.Ctrl | Modifiers.Alt, 'D', "Ctrl+Alt+D is already the pin key.")]
    [InlineData(Modifiers.Ctrl | Modifiers.Shift, 'T', "Ctrl+Shift+T is Reopen Closed Tab in browsers.")]
    [InlineData(Modifiers.Ctrl | Modifiers.Shift, 'Z', "Ctrl+Shift+Z is Redo in many apps.")]
    [InlineData(Modifiers.Alt, 'J', "Alt+J opens a menu in many apps.")]
    [InlineData(Modifiers.Ctrl, 'J', "Ctrl+J is a shortcut in most apps.")]
    public void KeysThatWouldBreakSomethingAreRefusedWithWhy(Modifiers modifiers, char key, string? why) =>
        Assert.Equal(why, Shortcuts.WhyNot(new Shortcut(modifiers, key), Typing.Nothing, Shortcuts.Pin, "pin"));

    [Fact]
    public void KeysThatTypeOrStartAnAccentOnAKeyboardAreRefused()
    {
        var q = new Shortcut(Modifiers.Ctrl | Modifiers.Alt, 'Q');
        Assert.Equal("On your keyboard Ctrl+Alt+Q types “@”.", Shortcuts.WhyNot(q, new Typing("@", false), null, "pin"));
        Assert.Equal(
            "On your keyboard Ctrl+Alt+Q starts an accent, as in é.", Shortcuts.WhyNot(q, new Typing(null, true), null, "pin"));
    }
}
