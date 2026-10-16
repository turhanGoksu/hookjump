"""The menu bar app: the hook, the line, the hotkeys and door 2 around a booth.

Everything runs on the main thread: a one-second timer ticks the booth, the
hotkeys and the door call jump and pin, and the menu is filled just before it
opens (changing a menu while it is open is what crashes rumps, issue #228).
"""

import sys
import traceback
from collections.abc import Callable

from AppKit import (
    NSApplication,
    NSFont,
    NSMenu,
    NSMenuItem,
    NSStatusBar,
    NSVariableStatusItemLength,
)
from Foundation import NSObject, NSRunLoop, NSRunLoopCommonModes, NSTimer
from PyObjCTools import AppHelper

from hookjump.adapters.carbon_hotkeys import CarbonHotkeys, HotkeyError
from hookjump.adapters.spotify import Spotify, SpotifyNotInstalled
from hookjump.adapters.storage import Store, default_path
from hookjump.booth import Booth
from hookjump.core.heatmap import favorite
from hookjump.door import DoorKeeper, knock
from hookjump.face import Frame, Mood
from hookjump.icon import hook_icon
from hookjump.shortcut import JUMP_DEFAULT, PIN_DEFAULT, Shortcut
from hookjump.words import clock, describe_jump, describe_pin

TICK_S = 1.0
_ACCESSORY = 1  # NSApplicationActivationPolicyAccessory: no Dock icon


class _Call(NSObject):
    """Objective-C target for timers, menu items and menu updates."""

    def initWithFunction_(self, function):  # noqa: N802
        self = self.init()
        self._function = function
        return self

    def fire_(self, _sender):  # noqa: N802
        self._function()

    def menuNeedsUpdate_(self, _menu):  # noqa: N802
        self._function()


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

        self._item = NSStatusBar.systemStatusBar().statusItemWithLength_(
            NSVariableStatusItemLength
        )
        button = self._item.button()
        button.setImagePosition_(2)  # the icon left of the line
        button.setFont_(NSFont.monospacedSystemFontOfSize_weight_(12.0, 0.0))
        self._menu = NSMenu.alloc().init()
        self._menu.setDelegate_(self._target(self._fill_menu))
        self._item.setMenu_(self._menu)

        self._keys = (Shortcut.parse(JUMP_DEFAULT), Shortcut.parse(PIN_DEFAULT))
        self._hotkeys: CarbonHotkeys | None = None
        try:
            self._hotkeys = CarbonHotkeys()
            self._hotkeys.bind(self._keys[0], self.jump)
            self._hotkeys.bind(self._keys[1], self.pin)
        except HotkeyError as error:
            self._hotkey_problem = f"{error}. Use the command: hookjump jump"
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
        at_ms = pinned.pin.at_ms if pinned.pin else None
        self._last_answer = describe_pin(at_ms)
        self._show_mood(self._booth.mood())
        self._play(pinned.frames)
        return {"pinned_ms": at_ms}

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

    def _fill_menu(self) -> None:
        self._menu.removeAllItems()
        reading = self._booth.reading
        if reading is None:
            self._note("Spotify is not running")
        elif reading.track_id is None:
            self._note("Nothing is playing in Spotify")
        else:
            self._note(f"{reading.title} — {reading.artist}")
            self._note(self._spot_line(reading.track_id))
        if self._last_answer:
            self._note(self._last_answer)
        for problem in (self._player_problem, self._hotkey_problem):
            if problem:
                self._note(f"⚠︎ {problem}")
        self._menu.addItem_(NSMenuItem.separatorItem())
        jump_key, pin_key = self._keys
        self._note(f"{jump_key}  jump to the best part")
        self._note(f"{pin_key}  pin this spot")
        self._menu.addItem_(NSMenuItem.separatorItem())
        self._action("Quit hookjump", self.quit, key="q")

    def _spot_line(self, track_id: str) -> str:
        pin = self._store.pin(track_id)
        if pin is not None:
            kind = "typed" if pin.typed else "pinned"
            return f"◆ {clock(pin.at_ms)} {kind}"
        found = favorite(self._store.seeks(track_id))
        if found is not None:
            return f"◆ {clock(found.target_ms)} learned from {found.listens} listens"
        if self._store.learning_paused():
            return "Learning is paused"
        return "Nothing learned for this song yet"

    def _note(self, text: str) -> None:
        item = NSMenuItem.alloc().initWithTitle_action_keyEquivalent_(text, None, "")
        item.setEnabled_(False)
        self._menu.addItem_(item)

    def _action(self, title: str, function: Callable[[], None], key: str = "") -> None:
        item = NSMenuItem.alloc().initWithTitle_action_keyEquivalent_(
            title, "fire:", key
        )
        item.setTarget_(self._target(function))
        self._menu.addItem_(item)

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
    MenuBar(store, Booth(store, spotify))
    jump_key, pin_key = Shortcut.parse(JUMP_DEFAULT), Shortcut.parse(PIN_DEFAULT)
    print(
        f"hookjump is running: {jump_key} jumps, {pin_key} pins. Quit from the hook.",
        flush=True,
    )
    AppHelper.runEventLoop(installInterrupt=True)
    return 0
