using System;
using System.Runtime.InteropServices;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// "New shortcut to jump", as on the Mac: press the new keys in Windows' own
/// hotkey box, see them, and see why if they cannot be taken. Save waits for
/// keys that can.
/// </summary>
internal static unsafe class ShortcutDialog
{
    public const int Now = 100, Box = 101, Problem = 102;

    private static Func<Shortcut, string?>? _check;
    private static Shortcut? _result;
    private static bool _modal;
    private static nint _open;

    /// <summary>
    /// Asks for new keys to <paramref name="what"/> ("jump" or "pin") and waits:
    /// the keys, or null when cancelled. <paramref name="check"/> says why keys
    /// cannot be taken, or null when they can.
    /// </summary>
    public static Shortcut? Ask(nint owner, string what, Shortcut? current, Func<Shortcut, string?> check)
    {
        if (_open != 0)
        {
            SetForegroundWindow(_open);
            return null;
        }
        (_check, _result, _modal) = (check, null, true);
        Prepare();
        fixed (ushort* template = Build(what, current))
            DialogBoxIndirectParam(GetModuleHandle(0), template, owner, &DialogProc, 0);
        (_check, _modal) = (null, false);
        return _result;
    }

    /// <summary>The dialog without its wait, hidden, for tests to answer with messages.</summary>
    internal static nint Open(string what, Shortcut? current, Func<Shortcut, string?> check)
    {
        (_check, _result) = (check, null);
        Prepare();
        fixed (ushort* template = Build(what, current))
            return CreateDialogIndirectParam(GetModuleHandle(0), template, 0, &DialogProc, 0);
    }

    internal static Shortcut? Result => _result;

    /// <summary>What the hotkey box holds: its key in the low byte, its modifiers above, in the box's own order.</summary>
    internal static Shortcut? FromBox(int value)
    {
        var key = (char)(value & 0xFF);
        var flags = (value >> 8) & 0xFF;
        if (key == 0) return null;
        var modifiers = Modifiers.None;
        if ((flags & 0x1) != 0) modifiers |= Modifiers.Shift; // HOTKEYF_SHIFT
        if ((flags & 0x2) != 0) modifiers |= Modifiers.Ctrl; // HOTKEYF_CONTROL
        if ((flags & 0x4) != 0) modifiers |= Modifiers.Alt; // HOTKEYF_ALT
        return new Shortcut(modifiers, key);
    }

    /// <summary>The words above the box: the Mac's.</summary>
    internal static string Says(Shortcut? current) =>
        (current is null ? "Now there is none." : $"Now it is {current}.")
        + " Combinations that would stop you typing a character, or that every app uses, are not accepted.";

    private static void Prepare()
    {
        var init = new InitCommonControls { Size = (uint)sizeof(InitCommonControls), Classes = IccHotkeyClass };
        InitCommonControlsEx(&init);
    }

    [UnmanagedCallersOnly]
    private static nint DialogProc(nint dialog, uint message, nint w, nint l)
    {
        switch (message)
        {
            case WmInitDialog:
                _open = dialog;
                EnableWindow(GetDlgItem(dialog, IdOk), false); // Save waits for keys that can be taken
                if (_modal) SetForegroundWindow(dialog);
                return 1; // the box, the first tab stop, gets the focus
            case WmCommand when (w & 0xFFFF) == Box && ((w >> 16) & 0xFFFF) == EnChange:
                Check(dialog);
                return 1;
            case WmCommand when (w & 0xFFFF) == IdOk:
                // Asked again: another app may have taken the keys meanwhile.
                if (Check(dialog) is { } keys)
                {
                    _result = keys;
                    EndDialog(dialog, 1);
                }
                return 1;
            case WmCommand when (w & 0xFFFF) == IdCancel:
                EndDialog(dialog, 0);
                return 1;
            case WmDestroy:
                _open = 0;
                return 0;
        }
        return 0;
    }

    /// <summary>Says why the keys in the box cannot be taken, and lets Save be pressed only when they can.</summary>
    private static Shortcut? Check(nint dialog)
    {
        var keys = FromBox((int)SendMessage(GetDlgItem(dialog, Box), HkmGetHotkey, 0, 0));
        var why = keys is null ? null : _check!(keys);
        SetDlgItemText(dialog, Problem, why ?? "");
        var ok = keys is not null && why is null;
        EnableWindow(GetDlgItem(dialog, IdOk), ok);
        return ok ? keys : null;
    }

    private static ushort[] Build(string what, Shortcut? current) => new DialogTemplate($"New shortcut to {what}", 200, 97)
        .Item(SsLeft, 7, 7, 186, 26, Now, StaticAtom, Says(current))
        .Item(WsBorder | WsTabStop, 7, 36, 186, 14, Box, HotkeyClass, "")
        .Item(SsLeft, 7, 54, 186, 18, Problem, StaticAtom, "")
        .Item(BsDefPushButton | WsTabStop, 89, 76, 50, 14, IdOk, ButtonAtom, "Save")
        .Item(BsPushButton | WsTabStop, 143, 76, 50, 14, IdCancel, ButtonAtom, "Cancel")
        .ToArray();
}
