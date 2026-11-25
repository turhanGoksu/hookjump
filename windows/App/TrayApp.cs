using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Hookjump.Storage;
using Microsoft.Win32;
using static Hookjump.App.Native;

namespace Hookjump.App;

/// <summary>
/// hookjump in the notification area: the hook, its menu, and the one thread
/// everything happens on.
/// </summary>
/// <remarks>
/// A hidden window gathers it all as Windows messages: Spotify's reports (which
/// arrive on Windows' own threads), a check every 30 s for a report that went
/// missing, quicker checks while a jump waits for Spotify, and the menu. So the
/// booth is only ever used from this thread, as on Android.
/// </remarks>
internal sealed unsafe class TrayApp : IDisposable
{
    private const uint WmHeard = WmApp + 1, WmTray = WmApp + 2;
    private const nint CheckTimer = 1, JumpTimer = 2, StripTimer = 3;
    private const uint CheckMs = 30_000, JumpCheckMs = 500, StripFrameMs = 16;
    private const uint IconId = 1;

    private static TrayApp? _running; // the window procedure finds the app here

    private readonly Booth _booth;
    private readonly Store _store;
    private readonly SmtcTurntable _turntable;
    private readonly nint _window;
    private readonly uint _taskbarCreated;
    private nint _icon;
    private Mood? _shownMood;
    private bool? _shownLight;
    private string? _shownTip;
    private string? _lastAnswer;
    private bool _jumpWaiting;
    private ShortcutChoice _shortcuts = new(null, null, null);
    private readonly StripWindow _strip = new();
    private readonly PanelWindow _panel;

    public TrayApp(Booth booth, Store store, SmtcTurntable turntable)
    {
        _booth = booth;
        _store = store;
        _turntable = turntable;
        _running = this;
        var name = "hookjump";
        fixed (char* className = name)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                Proc = &WindowProc,
                Instance = GetModuleHandle(0),
                ClassName = className,
            };
            RegisterClassEx(&windowClass);
        }
        // A hidden top-level window: unlike a message-only one, it hears
        // "TaskbarCreated" when Explorer restarts, and theme changes.
        _window = CreateWindowEx(0x80, name, name, 0, 0, 0, 0, 0, 0, 0, GetModuleHandle(0), 0);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        TakeShortcuts();
        _panel = new PanelWindow(Pressed);
        AddIcon();
        _turntable.Changed += () => PostMessage(_window, WmHeard, 0, 0);
        SetTimer(_window, CheckTimer, CheckMs, 0);
        Heard();
    }

    /// <summary>Runs until Quit; returns the exit code.</summary>
    public int Run()
    {
        Message message;
        while (GetMessage(&message, 0, 0, 0) > 0)
        {
            TranslateMessage(&message);
            DispatchMessage(&message);
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterHotKey(_window, Shortcuts.JumpId);
        UnregisterHotKey(_window, Shortcuts.PinId);
        var data = IconData(0);
        ShellNotifyIcon(NimDelete, &data);
        if (_icon != 0) DestroyIcon(_icon);
        _strip.Dispose();
        _panel.Dispose();
        DestroyWindow(_window);
        _running = null;
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint window, uint message, nint w, nint l)
    {
        try
        {
            if (_running is { } app && app.Handle(message, w, l)) return 0;
        }
        catch (Exception e)
        {
            // Nothing may escape into Windows; say it in the menu instead.
            if (_running is { } app) app._lastAnswer = $"Something went wrong: {e.Message}";
        }
        return DefWindowProc(window, message, w, l);
    }

    private bool Handle(uint message, nint w, nint l)
    {
        switch (message)
        {
            case WmHeard:
                Heard();
                return true;
            case WmTimer when w == CheckTimer || w == JumpTimer:
                Heard();
                return true;
            case WmTimer when w == StripTimer:
                if (!_strip.Frame()) KillTimer(_window, StripTimer);
                return true;
            case WmHotKey when w == Shortcuts.JumpId || w == Shortcuts.PinId:
                Do(w == Shortcuts.JumpId ? Command.Jump : Command.Pin);
                return true;
            case WmTray:
                // A click (or Enter on the icon) opens the sea panel; a right click, the menu.
                var what = (uint)(l & 0xFFFF);
                if (what is NinSelect or NinKeySelect) _panel.Toggle(_booth.PlayingChart(), _shortcuts);
                else if (what is WmContextMenu) ShowMenu(w);
                return true;
            case WmSettingChange:
                ShowHook(); // the taskbar may have turned light or dark
                return false;
            case WmDestroy:
                PostQuitMessage(0);
                return true;
        }
        if (message == _taskbarCreated)
        {
            // Explorer restarted: the icon is gone and must be added again.
            _shownMood = null;
            _shownTip = null;
            AddIcon();
            return true;
        }
        return false;
    }

    private void Heard()
    {
        _booth.Heard();
        ShowHook();
        _panel.Update(_booth.PlayingChart());
    }

    // The hook ------------------------------------------------------------------

    private void AddIcon()
    {
        var data = IconData(NifMessage);
        data.CallbackMessage = WmTray;
        ShellNotifyIcon(NimAdd, &data);
        data.Version = NotifyIconVersion4;
        ShellNotifyIcon(NimSetVersion, &data);
        ShowHook();
    }

    private void ShowHook()
    {
        var track = _booth.Reading?.TrackId;
        var mood = Moods.Of(track is not null, _store.LearningPaused(), _booth.SpotKnown());
        var light = LightTaskbar();
        var tip = track is null
            ? $"hookjump\n{Words.DescribeTarget(_booth.Decision())}"
            : $"{_booth.Reading!.Title}\n{Words.DescribeTarget(_booth.Decision())}";
        if (mood == _shownMood && light == _shownLight && tip == _shownTip) return;

        var data = IconData(NifIcon | NifTip | NifShowTip);
        var old = _icon;
        _icon = HookIcon.Draw(mood, light, GetSystemMetricsForDpi(SmCxSmIcon, GetDpiForSystem()));
        data.Icon = _icon;
        Copy(tip, data.Tip, 128);
        ShellNotifyIcon(NimModify, &data);
        if (old != 0) DestroyIcon(old);
        (_shownMood, _shownLight, _shownTip) = (mood, light, tip);
    }

    /// <summary>
    /// Says what a key did on the strip by the clock, for a few seconds; the
    /// words also stay in the menu. Not while the listener is busy with
    /// something full screen: a game, a film, a presentation. With the panel
    /// open, the panel says it, in the same corner.
    /// </summary>
    private void Tell(StripScene scene)
    {
        _lastAnswer = scene.Words;
        if (_panel.Visible)
        {
            _panel.Say(scene.Words);
            return;
        }
        SHQueryUserNotificationState(out var state);
        if (state is QunsBusy or QunsRunningD3DFullScreen or QunsPresentationMode) return;
        _strip.Show(scene);
        SetTimer(_window, StripTimer, StripFrameMs, 0);
    }

    private NotifyIconData IconData(uint flags) => new()
    {
        Size = (uint)sizeof(NotifyIconData),
        Window = _window,
        Id = IconId,
        Flags = flags,
    };

    // The shortcuts ------------------------------------------------------------------

    /// <summary>Takes the keys the listener chose, or the defaults, letting go of any held before.</summary>
    private void TakeShortcuts()
    {
        var window = _window;
        UnregisterHotKey(window, Shortcuts.JumpId);
        UnregisterHotKey(window, Shortcuts.PinId);
        _shortcuts = Shortcuts.Choose(
            Shortcut.FromSetting(_store.Shortcut("jump")),
            Shortcut.FromSetting(_store.Shortcut("pin")),
            key => TypingOf(key) != Typing.Nothing,
            (id, key) => RegisterHotKey(window, id, (uint)key.Modifiers | ModNoRepeat, key.VirtualKey),
            id => UnregisterHotKey(window, id));
    }

    /// <summary>New keys for "jump" or "pin", as the Mac asks for them.</summary>
    private void ChangeShortcut(string what)
    {
        var other = what == "jump" ? "pin" : "jump";
        var otherKeys = Shortcut.FromSetting(_store.Shortcut(other)) ?? (other == "jump" ? Shortcuts.Jump : Shortcuts.Pin);
        var current = what == "jump" ? _shortcuts.Jump : _shortcuts.Pin;
        // Let go of the keys while the dialog is open, or pressing the old
        // ones would jump instead of being recorded.
        UnregisterHotKey(_window, Shortcuts.JumpId);
        UnregisterHotKey(_window, Shortcuts.PinId);
        var chosen = ShortcutDialog.Ask(
            _window, what, current,
            keys => Shortcuts.WhyNot(keys, TypingOf(keys), otherKeys, other) ?? (Free(keys) ? null : $"Another app has {keys}."));
        if (chosen is not null) _store.SetShortcut(what, chosen.ToSetting());
        TakeShortcuts();
        if (chosen is not null) _lastAnswer = $"{(what == "jump" ? "Jump" : "Pin")} is {chosen} now.";
    }

    /// <summary>Whether Windows would give these keys to hookjump: asked by taking them for a moment.</summary>
    private bool Free(Shortcut keys)
    {
        const int trial = 3;
        if (!RegisterHotKey(_window, trial, (uint)keys.Modifiers | ModNoRepeat, keys.VirtualKey)) return false;
        UnregisterHotKey(_window, trial);
        return true;
    }

    /// <summary>
    /// What the shortcut does on the keyboard layouts the listener has: with
    /// AltGr, which is Ctrl+Alt, some type letters and signs, and some start
    /// an accent; a hotkey would take them away.
    /// </summary>
    private static Typing TypingOf(Shortcut key)
    {
        var layouts = stackalloc nint[32];
        var count = GetKeyboardLayoutList(32, layouts);
        var state = stackalloc byte[256];
        if (key.Modifiers.HasFlag(Modifiers.Ctrl)) state[0x11] = state[0xA2] = 0x80;
        if (key.Modifiers.HasFlag(Modifiers.Alt)) state[0x12] = state[0xA4] = 0x80;
        if (key.Modifiers.HasFlag(Modifiers.Shift)) state[0x10] = state[0xA0] = 0x80;
        var buffer = stackalloc char[8];
        for (var i = 0; i < count; i++)
        {
            var scan = MapVirtualKeyEx(key.VirtualKey, 0, layouts[i]);
            // Flag 4: ask without changing the keyboard's state (no dead key left waiting).
            var typed = ToUnicodeEx(key.VirtualKey, scan, state, buffer, 8, 4, layouts[i]);
            if (typed < 0) return new Typing(null, DeadKey: true);
            if (typed > 0) return new Typing(new string(buffer, 0, typed), DeadKey: false);
        }
        return Typing.Nothing;
    }

    private static bool LightTaskbar()
    {
        // 0 is a dark taskbar, Windows 11's default; a missing value counts as dark.
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    // The menu --------------------------------------------------------------------

    private void ShowMenu(nint anchor)
    {
        var track = _booth.Reading?.TrackId;
        var state = new TrayState(
            _booth.Reading?.Title,
            _booth.Reading?.Artist,
            track is not null,
            _booth.Decision(),
            track is not null && _store.Pin(track) is not null,
            track is not null && _store.IsIgnored(track),
            _store.LearningPaused(),
            _booth.Records(),
            _lastAnswer,
            _shortcuts,
            _store.Shortcut("jump") != 0 || _store.Shortcut("pin") != 0);
        var commands = new List<Command>();
        var menu = Fill(TrayMenu.Build(state), commands);
        // With version 4 the click's place comes in w; fall back to the pointer.
        var x = (short)(anchor & 0xFFFF);
        var y = (short)((anchor >> 16) & 0xFFFF);
        if (x == 0 && y == 0)
        {
            Point at;
            GetCursorPos(&at);
            (x, y) = ((short)at.X, (short)at.Y);
        }
        SetForegroundWindow(_window); // so the menu closes when clicked away from
        var chosen = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd | TpmNoNotify, x, y, _window, 0);
        PostMessage(_window, 0, 0, 0);
        DestroyMenu(menu);
        if (chosen > 0) Do(commands[chosen - 1]);
    }

    /// <summary>A Windows menu for these entries; a chosen entry's id is its place in <paramref name="commands"/> plus one.</summary>
    private static nint Fill(IReadOnlyList<MenuEntry> entries, List<Command> commands)
    {
        var menu = CreatePopupMenu();
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                AppendMenu(menu, MfSeparator, 0, null);
            }
            else if (entry.Children is { } children)
            {
                AppendMenu(menu, MfPopup, Fill(children, commands), Escape(entry.Text));
            }
            else if (entry.IsNote)
            {
                AppendMenu(menu, MfString | MfGrayed, 0, Escape(entry.Text));
            }
            else
            {
                commands.Add(entry.Command);
                // A tab puts the shortcut at the right of the entry, as Windows menus do.
                var text = entry.Keys is { } keys ? $"{Escape(entry.Text)}\t{keys}" : Escape(entry.Text);
                AppendMenu(menu, MfString | (entry.Checked ? MfChecked : 0), commands.Count, text);
            }
        }
        return menu;
    }

    // A Windows menu takes "&" as the mark of a shortcut letter.
    private static string Escape(string text) => text.Replace("&", "&&", StringComparison.Ordinal);

    private void Do(Command command)
    {
        switch (command)
        {
            case Command.Jump:
                // Where playback was when the key was pressed: the float starts there.
                var before = _booth.Reading;
                var from = before is null ? null : Booth.PositionNow(before, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                // The answer comes at once when there is nowhere to go, and
                // later, from Spotify's report, when there is.
                var answered = false;
                _booth.Jump(jumped =>
                {
                    answered = true;
                    Tell(Strip.ForJump(before, from, jumped));
                    StopJumpChecks();
                });
                if (!answered) StartJumpChecks();
                break;
            case Command.Pin:
                var pinned = _booth.Pin().Pin;
                Tell(Strip.ForPin(_booth.Reading, pinned));
                break;
            case Command.PinAtTime:
                if (_booth.Reading?.TrackId is not { } asked) break;
                // Spotify keeps reporting while the dialog is open, so the check sees the song as it is then.
                if (PinDialog.Ask(_window, typed => TypedPin.Check(typed, asked, _booth.Reading)) is { } at)
                    Tell(Strip.ForPin(_booth.Reading, _booth.PinAt(at).Pin));
                break;
            case Command.RemovePin:
                _lastAnswer = Words.DescribeUnpin(_booth.Unpin());
                break;
            case Command.ForgetLearned:
                _lastAnswer = Words.DescribeForgetLearned(_booth.ForgetLearnedFromSong());
                break;
            case Command.ForgetSong:
                _lastAnswer = Words.DescribeForget(_booth.ForgetSong());
                break;
            case Command.IgnoreSong:
                _lastAnswer = Words.DescribeIgnore(_booth.IgnoreSong());
                break;
            case Command.TrackAgain:
                _lastAnswer = Words.DescribeTrackAgain(_booth.TrackSongAgain());
                break;
            case Command.PauseLearning:
                var paused = !_store.LearningPaused();
                _booth.SetLearningPaused(paused);
                _lastAnswer = Words.DescribePause(paused);
                break;
            case Command.ChangeJumpKey:
                ChangeShortcut("jump");
                break;
            case Command.ChangePinKey:
                ChangeShortcut("pin");
                break;
            case Command.DefaultKeys:
                _store.SetShortcut("jump", 0);
                _store.SetShortcut("pin", 0);
                TakeShortcuts();
                _lastAnswer = $"Back to {Shortcuts.Jump} and {Shortcuts.Pin}.";
                break;
            case Command.ForgetAllLearned:
                var forget = MessageBox(
                    _window,
                    "Where you rewound or skipped to in every song, and the playback positions it learns from."
                    + " Your pins, the songs you asked it not to track and its settings stay. This cannot be undone.",
                    "Forget what hookjump learned?",
                    MbOkCancel | MbIconWarning | MbDefButton2);
                if (forget == IdOk)
                {
                    _booth.ForgetAllLearned();
                    _lastAnswer = Words.ForgotAllLearned;
                }
                break;
            case Command.DeleteEverything:
                var answer = MessageBox(
                    _window,
                    "Your pins, everything learned from your listening, the songs you asked it not to track"
                    + " and its settings. This cannot be undone.",
                    "Delete everything hookjump knows?",
                    MbOkCancel | MbIconWarning | MbDefButton2);
                if (answer == IdOk)
                {
                    _booth.ForgetEverything();
                    _lastAnswer = Words.ForgotEverything;
                }
                break;
            case Command.Quit:
                DestroyWindow(_window);
                return;
        }
        ShowHook();
        _panel.Update(_booth.PlayingChart());
    }

    /// <summary>What was pressed in the panel: the line pins its second, roughly, as a click on the Mac's line does.</summary>
    private void Pressed(PanelPart part, long? atMs)
    {
        switch (part)
        {
            case PanelPart.Line when atMs is { } at:
                Tell(Strip.ForPin(_booth.Reading, _booth.PinAt(at, exact: false).Pin));
                ShowHook();
                _panel.Update(_booth.PlayingChart());
                break;
            case PanelPart.Jump:
                Do(Command.Jump);
                break;
            case PanelPart.Pin:
                Do(Command.Pin);
                break;
            case PanelPart.RemovePin:
                Do(Command.RemovePin);
                if (_lastAnswer is { } answer) _panel.Say(answer);
                break;
            // The song buttons: Spotify's own report of the change is the answer.
            case PanelPart.Previous:
                _turntable.Control(SongControl.Previous);
                break;
            case PanelPart.PlayPause:
                _turntable.Control(SongControl.PlayPause);
                break;
            case PanelPart.Next:
                _turntable.Control(SongControl.Next);
                break;
        }
    }

    // While a jump waits for Spotify's word, look twice a second, so the needle
    // can try once more or report a slip even if Spotify stays silent.
    private void StartJumpChecks()
    {
        if (_jumpWaiting) return;
        _jumpWaiting = true;
        SetTimer(_window, JumpTimer, JumpCheckMs, 0);
    }

    private void StopJumpChecks()
    {
        if (!_jumpWaiting) return;
        _jumpWaiting = false;
        KillTimer(_window, JumpTimer);
    }
}
