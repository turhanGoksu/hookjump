using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// "Pin at a time", as on the Mac: a small dialog of Windows' own asks when the
/// favorite part starts. A time that cannot be pinned is said under the box,
/// and the dialog stays open to fix it.
/// </summary>
/// <remarks>
/// The dialog is built from a template in memory, so Windows' dialog manager
/// does the rest: Enter pins, Esc cancels, Tab moves, and it scales with the
/// monitor.
/// </remarks>
internal static unsafe class PinDialog
{
    public const string Title = "Pin at a time", Question = "When does your favorite part start?";
    public const int Box = 100, Problem = 101;

    private static readonly ushort[] Template = Build();
    private static Func<string, TypedPin>? _check;
    private static long? _result;
    private static bool _modal;
    private static nint _open;

    /// <summary>
    /// Asks for a time and waits for the answer: the time to pin, or null when
    /// cancelled. Asked again while open, it brings the open one forward.
    /// </summary>
    public static long? Ask(nint owner, Func<string, TypedPin> check)
    {
        if (_open != 0)
        {
            SetForegroundWindow(_open);
            return null;
        }
        _check = check;
        _result = null;
        _modal = true;
        fixed (ushort* template = Template)
            DialogBoxIndirectParam(GetModuleHandle(0), template, owner, &DialogProc, 0);
        _modal = false;
        _check = null;
        return _result;
    }

    /// <summary>The dialog without its wait, hidden, for tests to answer with WM_COMMAND.</summary>
    internal static nint Open(Func<string, TypedPin> check)
    {
        _check = check;
        _result = null;
        fixed (ushort* template = Template)
            return CreateDialogIndirectParam(GetModuleHandle(0), template, 0, &DialogProc, 0);
    }

    /// <summary>What the last answer came to.</summary>
    internal static long? Result => _result;

    [UnmanagedCallersOnly]
    private static nint DialogProc(nint dialog, uint message, nint w, nint l)
    {
        switch (message)
        {
            case WmInitDialog:
                _open = dialog;
                var box = GetDlgItem(dialog, Box);
                SendMessage(box, EmLimitText, 16, 0);
                fixed (char* cue = "1:23") SendMessage(box, EmSetCueBanner, 1, (nint)cue);
                // Opened from the menu, it comes to the front, ready to type in.
                if (_modal) SetForegroundWindow(dialog);
                return 1; // the box, the first tab stop, gets the focus
            case WmCommand when (w & 0xFFFF) == IdOk:
                var answer = _check!(Typed(dialog));
                if (answer.Problem is { } problem)
                {
                    SetDlgItemText(dialog, Problem, problem);
                    var typed = GetDlgItem(dialog, Box);
                    SendMessage(typed, EmSetSel, 0, -1);
                    SendMessage(dialog, WmNextDlgCtl, typed, 1);
                    return 1;
                }
                _result = answer.AtMs;
                EndDialog(dialog, 1);
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

    private static string Typed(nint dialog)
    {
        var text = stackalloc char[32];
        var length = GetDlgItemText(dialog, Box, text, 32);
        return new string(text, 0, (int)length);
    }

    /// <summary>
    /// The dialog in dialog units (a quarter of the font's average width, an
    /// eighth of its height), laid out by Windows' own spacing: 7 from the
    /// edges, 50 by 14 buttons.
    /// </summary>
    internal static ushort[] Build()
    {
        var t = new List<ushort>();
        void Word(int value) => t.Add((ushort)value);
        void Dword(uint value)
        {
            Word((int)(value & 0xFFFF));
            Word((int)(value >> 16));
        }
        void Text(string text)
        {
            foreach (var c in text) Word(c);
            Word(0);
        }
        void Item(uint style, int x, int y, int width, int height, int id, ushort kind, string text)
        {
            if (t.Count % 2 == 1) Word(0); // each item starts on a 4-byte boundary
            Dword(style | WsChild | WsVisible);
            Dword(0);
            foreach (var v in new[] { x, y, width, height, id }) Word(v);
            Word(0xFFFF); // a class Windows knows, by number
            Word(kind);
            Text(text);
            Word(0); // no creation data
        }

        Dword(WsPopup | WsCaption | WsSysMenu | DsModalFrame | DsSetFont | DsCenter);
        Dword(0);
        Word(5); // items
        foreach (var v in new[] { 0, 0, 200, 79 }) Word(v);
        Word(0); // no menu
        Word(0); // the standard dialog class
        Text(Title);
        Word(9);
        Text("Segoe UI");
        Item(SsLeft, 7, 7, 186, 10, -1, StaticAtom, Question);
        Item(EsAutoHScroll | WsBorder | WsTabStop, 7, 20, 70, 14, Box, EditAtom, "");
        Item(SsLeft, 7, 38, 186, 18, Problem, StaticAtom, "");
        Item(BsDefPushButton | WsTabStop, 89, 58, 50, 14, IdOk, ButtonAtom, "Pin");
        Item(BsPushButton | WsTabStop, 143, 58, 50, 14, IdCancel, ButtonAtom, "Cancel");
        return t.ToArray();
    }
}
