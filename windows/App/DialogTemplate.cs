using System.Collections.Generic;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// A dialog of Windows' own, written as the template Windows reads from
/// memory (DLGTEMPLATE), so no resource file is needed. Sizes are dialog
/// units: a quarter of the font's average width, an eighth of its height.
/// Windows' own spacing: 7 from the edges, 50 by 14 buttons.
/// </summary>
internal sealed class DialogTemplate
{
    private readonly List<ushort> _words = [];
    private readonly int _countAt;
    private ushort _count;

    public DialogTemplate(string title, int width, int height)
    {
        Dword(WsPopup | WsCaption | WsSysMenu | DsModalFrame | DsSetFont | DsCenter);
        Dword(0);
        _countAt = _words.Count;
        Word(0); // how many items: filled in as they come
        foreach (var v in new[] { 0, 0, width, height }) Word(v);
        Word(0); // no menu
        Word(0); // the standard dialog class
        Text(title);
        Word(9);
        Text("Segoe UI");
    }

    /// <summary>A control of a class Windows knows by number: a button, an edit box, a static text.</summary>
    public DialogTemplate Item(uint style, int x, int y, int width, int height, int id, ushort kind, string text)
    {
        Start(style, x, y, width, height, id);
        Word(0xFFFF);
        Word(kind);
        return End(text);
    }

    /// <summary>A control of a class known by name, such as the hotkey box.</summary>
    public DialogTemplate Item(uint style, int x, int y, int width, int height, int id, string className, string text)
    {
        Start(style, x, y, width, height, id);
        Text(className);
        return End(text);
    }

    public ushort[] ToArray()
    {
        _words[_countAt] = _count;
        return [.. _words];
    }

    private void Start(uint style, int x, int y, int width, int height, int id)
    {
        if (_words.Count % 2 == 1) Word(0); // each item starts on a 4-byte boundary
        Dword(style | WsChild | WsVisible);
        Dword(0);
        foreach (var v in new[] { x, y, width, height, id }) Word(v);
        _count++;
    }

    private DialogTemplate End(string text)
    {
        Text(text);
        Word(0); // no creation data
        return this;
    }

    private void Word(int value) => _words.Add((ushort)value);

    private void Dword(uint value)
    {
        Word((int)(value & 0xFFFF));
        Word((int)(value >> 16));
    }

    private void Text(string text)
    {
        foreach (var c in text) Word(c);
        Word(0);
    }
}
