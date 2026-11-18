using System;
using System.Collections.Generic;

namespace Hookjump.App;

/// <summary>A hotkey's modifiers, as RegisterHotKey numbers them.</summary>
[Flags]
public enum Modifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A key with its modifiers; letters and digits are their own virtual-key codes.</summary>
public sealed record Shortcut(Modifiers Modifiers, char Key)
{
    public uint VirtualKey => Key;

    /// <summary>The way Windows writes it: Ctrl+Alt+Shift+F.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(Modifiers.Win)) parts.Add("Win");
        if (Modifiers.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}

/// <summary>The jump and pin shortcuts in use, or none, and what to tell the listener about it.</summary>
public sealed record ShortcutChoice(Shortcut? Jump, Shortcut? Pin, string? Note);

/// <summary>
/// Which hotkeys to use: the Mac's letters (F for the favorite part, D to drop
/// anchor) with Ctrl+Alt, or else with Ctrl+Alt+Shift; never a key that types a
/// character on one of the listener's keyboards, as AltGr does on some.
/// </summary>
public static class Shortcuts
{
    public const int JumpId = 1, PinId = 2;

    public static readonly Shortcut Jump = new(Modifiers.Ctrl | Modifiers.Alt, 'F');
    public static readonly Shortcut Pin = new(Modifiers.Ctrl | Modifiers.Alt, 'D');
    public static readonly Shortcut JumpFallback = new(Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift, 'F');
    public static readonly Shortcut PinFallback = new(Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift, 'D');

    /// <summary>
    /// The first pair that types nothing and that Windows lets hookjump have.
    /// <paramref name="register"/> takes an id and a shortcut; a pair that only
    /// half works is let go again with <paramref name="unregister"/>.
    /// </summary>
    public static ShortcutChoice Choose(
        Func<Shortcut, bool> typesCharacter, Func<int, Shortcut, bool> register, Action<int> unregister)
    {
        var why = Problem(Jump, Pin, typesCharacter, register, unregister);
        if (why is null) return new ShortcutChoice(Jump, Pin, null);
        var fallbackWhy = Problem(JumpFallback, PinFallback, typesCharacter, register, unregister);
        if (fallbackWhy is null)
            return new ShortcutChoice(JumpFallback, PinFallback, $"{Capital(why)}, so {JumpFallback} and {PinFallback} are used.");
        return new ShortcutChoice(null, null, $"No shortcuts: {why}, and {fallbackWhy}. Jump and Pin are in this menu.");
    }

    private static string? Problem(
        Shortcut jump, Shortcut pin, Func<Shortcut, bool> typesCharacter, Func<int, Shortcut, bool> register,
        Action<int> unregister)
    {
        foreach (var key in new[] { jump, pin })
        {
            if (typesCharacter(key)) return $"{key} types a character on your keyboard";
        }
        if (!register(JumpId, jump)) return $"another app has {jump}";
        if (!register(PinId, pin))
        {
            unregister(JumpId);
            return $"another app has {pin}";
        }
        return null;
    }

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
