using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>As the settings keep it (spec/STORAGE.md): the virtual-key code above the modifier mask.</summary>
    public long ToSetting() => ((long)Key << 16) | (long)Modifiers;

    /// <summary>A kept shortcut, or null for the default (0) or for anything that is not a letter or a digit with modifiers.</summary>
    public static Shortcut? FromSetting(long value)
    {
        var key = (char)((value >> 16) & 0xFFFF);
        var modifiers = (Modifiers)(value & 0xFFFF);
        if (!IsKey(key) || modifiers == Modifiers.None || (modifiers & ~(Modifiers.Alt | Modifiers.Ctrl | Modifiers.Shift | Modifiers.Win)) != 0)
            return null;
        return new Shortcut(modifiers, key);
    }

    /// <summary>The keys a shortcut can end in: a letter or a digit, the same on every keyboard.</summary>
    public static bool IsKey(char key) => key is >= 'A' and <= 'Z' or >= '0' and <= '9';
}

/// <summary>What a combination does on the listener's keyboards: the character it types on one of them, or that it starts an accent.</summary>
public sealed record Typing(string? Typed, bool DeadKey)
{
    public static readonly Typing Nothing = new(null, false);
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
        Func<Shortcut, bool> typesCharacter, Func<int, Shortcut, bool> register, Action<int> unregister) =>
        Choose(null, null, typesCharacter, register, unregister);

    /// <summary>
    /// The shortcuts the listener chose, where they did; the defaults for the
    /// rest. Only the defaults fall back: a chosen shortcut is the listener's,
    /// never swapped silently. If it cannot be had, the menu says so, as on the Mac.
    /// </summary>
    public static ShortcutChoice Choose(
        Shortcut? chosenJump, Shortcut? chosenPin, Func<Shortcut, bool> typesCharacter, Func<int, Shortcut, bool> register,
        Action<int> unregister)
    {
        if (chosenJump is not null || chosenPin is not null)
        {
            var jump = chosenJump ?? Jump;
            var pin = chosenPin ?? Pin;
            string? Held(int id, Shortcut key) =>
                typesCharacter(key) ? $"{key} now types a character on your keyboard"
                : register(id, key) ? null
                : $"another app has {key}";
            var problems = new[] { Held(JumpId, jump), Held(PinId, pin) };
            var note = problems.Where(p => p is not null).ToList() is { Count: > 0 } found
                ? $"{Capital(string.Join(", and ", found))}: change {(found.Count == 1 ? "it" : "them")} in Shortcuts."
                : null;
            return new ShortcutChoice(problems[0] is null ? jump : null, problems[1] is null ? pin : null, note);
        }
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

    // Shortcuts most apps use with Ctrl+Shift; Ctrl alone or Alt alone with a
    // letter is refused whole, as nearly every letter is some app's.
    private static readonly Dictionary<Shortcut, string> Reserved = new()
    {
        [new(Modifiers.Ctrl | Modifiers.Shift, 'Z')] = "Redo in many apps",
        [new(Modifiers.Ctrl | Modifiers.Shift, 'S')] = "Save As in many apps",
        [new(Modifiers.Ctrl | Modifiers.Shift, 'V')] = "Paste as Plain Text in many apps",
        [new(Modifiers.Ctrl | Modifiers.Shift, 'T')] = "Reopen Closed Tab in browsers",
        [new(Modifiers.Ctrl | Modifiers.Shift, 'N')] = "New Private Window in browsers",
        [new(Modifiers.Ctrl | Modifiers.Shift, 'I')] = "Developer Tools in browsers",
    };

    /// <summary>
    /// Why a shortcut the listener pressed would break something, or null if
    /// it can be taken; the Mac's rules (hookjump.shortcut.why_not), with
    /// Windows' own shortcuts. Whether another app holds it is asked of
    /// Windows apart.
    /// </summary>
    /// <param name="typing">What the combination does on the listener's keyboards.</param>
    /// <param name="other">The other hookjump shortcut, and what it does ("jump" or "pin").</param>
    public static string? WhyNot(Shortcut key, Typing typing, Shortcut? other, string otherName)
    {
        var withoutShift = key.Modifiers & ~Modifiers.Shift;
        if (!Shortcut.IsKey(key.Key)) return "Use a letter or a digit, with Ctrl or Alt.";
        if ((withoutShift & (Modifiers.Ctrl | Modifiers.Alt)) == 0) return $"{key} alone is typing: hold Ctrl or Alt with it.";
        if (key == other) return $"{key} is already the {otherName} key.";
        if (Reserved.TryGetValue(key, out var use)) return $"{key} is {use}.";
        if (key.Modifiers == Modifiers.Alt) return $"{key} opens a menu in many apps.";
        if (key.Modifiers == Modifiers.Ctrl) return $"{key} is a shortcut in most apps.";
        if (typing.DeadKey) return $"On your keyboard {key} starts an accent, as in é.";
        if (typing.Typed is { } typed) return $"On your keyboard {key} types “{typed}”.";
        return null;
    }
}
