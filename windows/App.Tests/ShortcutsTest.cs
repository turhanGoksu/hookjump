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
}
