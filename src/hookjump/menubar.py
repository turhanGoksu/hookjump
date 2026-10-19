"""The menu bar app: the hook, the line, the hotkeys and door 2 around a booth.

Everything runs on the main thread: a one-second timer ticks the booth, the
hotkeys and the door call jump and pin, and the menu is filled just before it
opens (changing a menu while it is open is what crashes rumps, issue #228).
"""

import os
import sys
import traceback
from collections.abc import Callable
from pathlib import Path

from AppKit import (
    NSAlert,
    NSApplication,
    NSFont,
    NSMakeRect,
    NSMenu,
    NSMenuItem,
    NSStatusBar,
    NSTextField,
    NSVariableStatusItemLength,
)
from Foundation import NSObject, NSRunLoop, NSRunLoopCommonModes, NSTimer
from PyObjCTools import AppHelper

from hookjump import app_bundle
from hookjump.adapters.carbon_hotkeys import CarbonHotkeys, HotkeyError
from hookjump.adapters.spotify import Spotify, SpotifyNotInstalled
from hookjump.adapters.storage import Store, default_path
from hookjump.booth import Booth
from hookjump.core.heatmap import favorite
from hookjump.door import DoorKeeper, knock
from hookjump.face import Frame, Mood
from hookjump.icon import hook_icon
from hookjump.recorder import record
from hookjump.shortcut import JUMP_DEFAULT, PIN_DEFAULT, Shortcut
from hookjump.sleeve import Sleeve, sleeve_for
from hookjump.sleeve_view import HEIGHT, WIDTH, SleeveView
from hookjump.words import describe_jump, describe_pin, parse_clock

TICK_S = 1.0
_NAMES = ("jump", "pin")
_DEFAULTS = (Shortcut.parse(JUMP_DEFAULT), Shortcut.parse(PIN_DEFAULT))
_ACCESSORY = 1  # NSApplicationActivationPolicyAccessory: no Dock icon
_FIRST_BUTTON = 1000  # NSAlertFirstButtonReturn
_CRITICAL = 2  # NSAlertStyleCritical


class _Call(NSObject):
    """Objective-C target for timers and menu items: calls a Python function."""

    def initWithFunction_(self, function):  # noqa: N802
        self = self.init()
        self._function = function
        return self

    def fire_(self, _sender):  # noqa: N802
        self._function()


class _MenuWatch(NSObject):
    """The menu's delegate: fill it just before it opens, know while it is open."""

    def initWithOwner_(self, owner):  # noqa: N802
        self = self.init()
        self._owner = owner
        return self

    def menuNeedsUpdate_(self, _menu):  # noqa: N802
        self._owner.fill_menu()

    def menuWillOpen_(self, _menu):  # noqa: N802
        self._owner.menu_open = True

    def menuDidClose_(self, _menu):  # noqa: N802
        self._owner.menu_open = False


class MenuBar:
    def __init__(self, store: Store, booth: Booth) -> None:
        self._store = store
        self._booth = booth
        self._player_problem: str | None = None
        self._hotkey_problem: str | None = None
        self._last_answer: str | None = None
        self._generation = 0
        self._mood: Mood | None = None
        self._keep: list[NSObject] = []  # Objective-C targets must stay alive
        # Targets of the current menu only: replaced at every fill, so opening
        # the menu all day does not pile them up (the leak in rumps, #232).
        self._menu_targets: list[NSObject] = []
        self.menu_open = False

        self._item = NSStatusBar.systemStatusBar().statusItemWithLength_(
            NSVariableStatusItemLength
        )
        button = self._item.button()
        button.setImagePosition_(2)  # the icon left of the line
        button.setFont_(NSFont.monospacedSystemFontOfSize_weight_(12.0, 0.0))
        self._menu = NSMenu.alloc().init()
        watch = _MenuWatch.alloc().initWithOwner_(self)
        self._keep.append(watch)
        self._menu.setDelegate_(watch)
        self._item.setMenu_(self._menu)
        self._sleeve_view = SleeveView.alloc().initWithFrame_(
            NSMakeRect(0, 0, WIDTH, HEIGHT)
        )
        self._sleeve_view.on_pick = self._pin_clicked

        self._hotkeys: CarbonHotkeys | None = None
        try:
            self._hotkeys = CarbonHotkeys()
        except HotkeyError as error:
            self._hotkey_problem = f"{error}. Use the command: hookjump jump"
        stored = (self._store.shortcut(name) for name in _NAMES)
        self.keys = tuple(
            Shortcut.from_int(value) if value else default
            for value, default in zip(stored, _DEFAULTS, strict=True)
        )
        self._bind(self.keys)
        self._door = DoorKeeper(self._on_knock)

        # In the common modes, not only the default one: a scheduled timer stops
        # while a menu is open, and the first try lost 3-11 s of samples so.
        timer = NSTimer.timerWithTimeInterval_target_selector_userInfo_repeats_(
            TICK_S, self._target(self.tick), "fire:", None, True
        )
        NSRunLoop.currentRunLoop().addTimer_forMode_(timer, NSRunLoopCommonModes)
        self.tick()

    # Called every second, and by the hotkeys and the door ------------------

    def tick(self) -> None:
        try:
            self._booth.tick()
            self._player_problem = None
        except PermissionError as error:
            self._player_problem = str(error)
        except Exception:  # noqa: BLE001  one bad second must not stop the app
            traceback.print_exc()
        self._show_mood(Mood.RESTING if self._player_problem else self._booth.mood())
        if self.menu_open:  # the ● moves along while the menu is open
            self._sleeve_view.show(self._sleeve())

    def jump(self) -> dict:
        try:
            jumped = self._booth.jump()
        except PermissionError as error:
            self._player_problem = str(error)
            return {"status": "player_unavailable"}
        result = jumped.result
        landing = jumped.landing.value if jumped.landing else None
        self._last_answer = describe_jump(
            result.status.value, result.target_ms, landing
        )
        self._play(jumped.frames)
        return {
            "status": result.status.value,
            "target_ms": result.target_ms,
            "landing": landing,
        }

    def pin(self) -> dict:
        try:
            pinned = self._booth.pin()
        except PermissionError as error:
            self._player_problem = str(error)
            return {}
        return self._pinned(pinned)

    def quit(self) -> None:
        if self._hotkeys:
            self._hotkeys.unbind_all()
        self._door.close()
        self._store.close()
        NSStatusBar.systemStatusBar().removeStatusItem_(self._item)
        AppHelper.stopEventLoop()

    # The face and the menu ----------------------------------------------------

    def _on_knock(self, action: str) -> dict:
        return self.jump() if action == "jump" else self.pin()

    def _show_mood(self, mood: Mood) -> None:
        if mood is not self._mood:
            self._item.button().setImage_(hook_icon(mood))
            self._mood = mood

    def _play(self, frames: list[Frame]) -> None:
        self._generation += 1
        mine = self._generation

        def show(index: int) -> None:
            if mine != self._generation or index == len(frames):
                return
            text = frames[index].text
            self._item.button().setTitle_(f" {text}" if text else "")
            AppHelper.callLater(frames[index].seconds, show, index + 1)

        show(0)

    # The menu: the record sleeve, then what can be done with it -------------

    def fill_menu(self) -> None:
        self._menu.removeAllItems()
        self._menu_targets = []
        sleeve_item = NSMenuItem.alloc().init()
        self._sleeve_view.show(self._sleeve())
        sleeve_item.setView_(self._sleeve_view)
        self._menu.addItem_(sleeve_item)

        notes = [self._last_answer, self._player_problem, self._hotkey_problem]
        for index, note in enumerate(notes):
            if note:
                self._note(self._menu, note if index == 0 else f"⚠︎ {note}")
        self._menu.addItem_(NSMenuItem.separatorItem())

        reading = self._booth.reading
        track = reading.track_id if reading else None
        if track is not None:
            song = NSMenu.alloc().init()
            self._action(song, "Pin at a Time…", lambda: self._pin_typed(track))
            if self._store.pin(track) is not None:
                self._action(song, "Remove the Pin", lambda: self._remove_pin(track))
            self._action(song, "Forget This Song", lambda: self._forget(track))
            if self._store.is_ignored(track):
                self._action(song, "Track This Song Again", lambda: self._track(track))
            else:
                self._action(song, "Don't Track This Song", lambda: self._ignore(track))
            item = NSMenuItem.alloc().initWithTitle_action_keyEquivalent_(
                "This Song", None, ""
            )
            item.setSubmenu_(song)
            self._menu.addItem_(item)
        pause = self._action(self._menu, "Pause Learning", self._toggle_pause)
        pause.setState_(1 if self._store.learning_paused() else 0)

        self._menu.addItem_(NSMenuItem.separatorItem())
        jump_key, pin_key = self.keys
        keys = NSMenu.alloc().init()
        self._action(
            keys, f"Change the Jump Shortcut ({jump_key})…", lambda: self._change(0)
        )
        self._action(
            keys, f"Change the Pin Shortcut ({pin_key})…", lambda: self._change(1)
        )
        if self.keys != _DEFAULTS:
            self._action(
                keys, "Reset to {} and {}".format(*_DEFAULTS), self._reset_keys
            )
        item = NSMenuItem.alloc().initWithTitle_action_keyEquivalent_(
            f"Shortcuts: {jump_key} jump · {pin_key} pin", None, ""
        )
        item.setSubmenu_(keys)
        self._menu.addItem_(item)
        login = self._action(self._menu, "Open at Login", self._toggle_login)
        bundle = self._bundle()
        login.setState_(1 if bundle and app_bundle.opens_at_login(bundle) else 0)
        self._action(self._menu, "Delete Everything…", self._delete_everything)
        self._action(self._menu, "Quit hookjump", self.quit, key="q")

    def _sleeve(self) -> Sleeve | None:
        reading = self._booth.reading
        if reading is None or reading.track_id is None:
            return None
        seeks = self._store.seeks(reading.track_id)
        return sleeve_for(
            reading, self._store.pin(reading.track_id), favorite(seeks), seeks
        )

    # What the menu does ---------------------------------------------------------

    def _pin_clicked(self, ms: int) -> None:
        self._pinned(self._booth.pin_at(ms, exact=False))

    def _pin_typed(self, track: str) -> None:
        text = self._ask("Pin at a time", "When does your favorite part start?")
        if text is None:
            return
        ms = parse_clock(text)
        if ms is None:
            self._last_answer = f"“{text}” is not a time. Try 1:23."
        elif not self._booth.reading or self._booth.reading.track_id != track:
            self._last_answer = "The song changed meanwhile, so nothing was pinned."
        else:
            self._pinned(self._booth.pin_at(ms, exact=True))

    def _pinned(self, pinned) -> dict:  # Pinned -> the door's reply
        at_ms = pinned.pin.at_ms if pinned.pin else None
        exact = bool(pinned.pin and pinned.pin.typed)
        self._last_answer = describe_pin(at_ms, exact)
        self._show_mood(self._booth.mood())
        self._play(pinned.frames)
        return {"pinned_ms": at_ms}

    def _remove_pin(self, track: str) -> None:
        self._store.clear_pin(track)
        self._last_answer = "Pin removed."
        self._show_mood(self._booth.mood())

    def _forget(self, track: str) -> None:
        self._store.forget_track(track)
        self._last_answer = "This song's pin and everything learned from it are gone."
        self._show_mood(self._booth.mood())

    def _ignore(self, track: str) -> None:
        self._store.ignore(track)
        self._last_answer = "hookjump will not learn from this song. Its pin stays."
        self._show_mood(self._booth.mood())

    def _track(self, track: str) -> None:
        self._store.unignore(track)
        self._last_answer = "hookjump learns from this song again."

    def _toggle_pause(self) -> None:
        paused = not self._store.learning_paused()
        self._store.set_learning_paused(paused)
        self._last_answer = "Learning paused." if paused else "Learning again."
        self._show_mood(self._booth.mood())

    def _bundle(self) -> Path | None:
        """hookjump.app, when this runs from it (its launcher says so)."""
        path = os.environ.get("HOOKJUMP_BUNDLE")
        return Path(path) if path else None

    def _toggle_login(self) -> None:
        bundle = self._bundle()
        if bundle is None:
            self._tell(
                "Open at login needs hookjump.app",
                "hookjump is running from a terminal now. Run hookjump install"
                " once; then open hookjump.app and turn this on in its menu.",
            )
            return
        on = not app_bundle.opens_at_login(bundle)
        try:
            app_bundle.open_at_login(on, bundle)
        except app_bundle.InstallError as error:
            self._last_answer = str(error)
            return
        self._last_answer = (
            "hookjump opens when you log in."
            if on
            else "hookjump no longer opens at login."
        )

    def _delete_everything(self) -> None:
        if self._confirm(
            "Delete everything hookjump knows?",
            "Your pins, everything learned from your listening, the songs you asked"
            " it not to track and its settings. This cannot be undone.",
            "Delete Everything",
        ):
            self._store.forget_all()
            self._last_answer = "Everything was deleted."
            self.keys = _DEFAULTS  # the stored shortcuts went with the settings
            self._bind(self.keys)
            self._show_mood(self._booth.mood())

    # Shortcuts --------------------------------------------------------------------

    def _bind(self, keys: tuple[Shortcut, Shortcut]) -> bool:
        if self._hotkeys is None:
            return False
        self._hotkeys.unbind_all()
        try:
            self._hotkeys.bind(keys[0], self.jump)
            self._hotkeys.bind(keys[1], self.pin)
        except HotkeyError as error:
            self._hotkeys.unbind_all()
            self._hotkey_problem = f"{error}. Use the command: hookjump jump"
            return False
        self._hotkey_problem = None
        return True

    def _change(self, index: int) -> None:
        old = self.keys
        if self._hotkeys:
            self._hotkeys.unbind_all()  # or pressing the old one would jump
        what = ("jump to the best part", "pin the playing spot")[index]
        new = record(what, old[index], old[1 - index])
        if new is None:
            self._bind(old)
            return
        keys = (new, old[1]) if index == 0 else (old[0], new)
        if self._bind(keys):
            self.keys = keys
            self._store.set_shortcut(_NAMES[index], new.to_int())
            self._last_answer = f"{new} now {('jumps', 'pins')[index]}."
        else:  # macOS refused it, maybe another app has it: keep the old pair
            self._bind(old)
            self._last_answer = f"macOS did not accept {new}; {old[index]} stays."

    def _reset_keys(self) -> None:
        for name in _NAMES:
            self._store.set_shortcut(name, 0)
        self.keys = _DEFAULTS
        self._bind(self.keys)
        self._last_answer = "Shortcuts are {} and {} again.".format(*_DEFAULTS)

    # Small helpers ----------------------------------------------------------------

    def _ask(self, title: str, question: str) -> str | None:
        alert = NSAlert.alloc().init()
        alert.setMessageText_(title)
        alert.setInformativeText_(question)
        alert.addButtonWithTitle_("Pin")
        alert.addButtonWithTitle_("Cancel")
        field = NSTextField.alloc().initWithFrame_(NSMakeRect(0, 0, 140, 24))
        field.setPlaceholderString_("1:23")
        alert.setAccessoryView_(field)
        alert.window().setInitialFirstResponder_(field)
        # An app without a Dock icon must step forward, or the alert opens
        # behind the other windows (what happens to rumps, issue #225).
        NSApplication.sharedApplication().activateIgnoringOtherApps_(True)
        if alert.runModal() != _FIRST_BUTTON:
            return None
        return str(field.stringValue())

    def _tell(self, title: str, text: str) -> None:
        alert = NSAlert.alloc().init()
        alert.setMessageText_(title)
        alert.setInformativeText_(text)
        NSApplication.sharedApplication().activateIgnoringOtherApps_(True)
        alert.runModal()

    def _confirm(self, title: str, text: str, button: str) -> bool:
        alert = NSAlert.alloc().init()
        alert.setAlertStyle_(_CRITICAL)
        alert.setMessageText_(title)
        alert.setInformativeText_(text)
        alert.addButtonWithTitle_(button)
        alert.addButtonWithTitle_("Cancel")
        NSApplication.sharedApplication().activateIgnoringOtherApps_(True)
        return alert.runModal() == _FIRST_BUTTON

    def _note(self, menu: NSMenu, text: str) -> None:
        item = NSMenuItem.alloc().initWithTitle_action_keyEquivalent_(text, None, "")
        item.setEnabled_(False)
        menu.addItem_(item)

    def _action(
        self, menu: NSMenu, title: str, function: Callable[[], None], key: str = ""
    ) -> NSMenuItem:
        item = NSMenuItem.alloc().initWithTitle_action_keyEquivalent_(
            title, "fire:", key
        )
        target = _Call.alloc().initWithFunction_(function)
        self._menu_targets.append(target)
        item.setTarget_(target)
        menu.addItem_(item)
        return item

    def _target(self, function: Callable[[], None]) -> NSObject:
        target = _Call.alloc().initWithFunction_(function)
        self._keep.append(target)
        return target


def run() -> int:
    """Start the menu bar app; returns when it quits."""
    if knock("ping", timeout_s=0.5) is not None:
        print("hookjump is already running.", file=sys.stderr)
        return 1
    try:
        spotify = Spotify()
    except SpotifyNotInstalled:
        print("hookjump needs the Spotify desktop app.", file=sys.stderr)
        return 1
    NSApplication.sharedApplication().setActivationPolicy_(_ACCESSORY)
    store = Store(default_path())
    bar = MenuBar(store, Booth(store, spotify))
    jump_key, pin_key = bar.keys
    print(
        f"hookjump is running: {jump_key} jumps, {pin_key} pins. Quit from the hook.",
        flush=True,
    )
    AppHelper.runEventLoop(installInterrupt=True)
    return 0
