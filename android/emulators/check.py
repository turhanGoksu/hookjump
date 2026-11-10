"""Check hookjump on Android emulators, one Android version at a time.

Spotify is not on emulators, so the test player (android/testplayer) stands in
for it; only a debug build of hookjump reads it. For each version this script
boots an emulator without a window, installs both apps and checks that:

- hookjump sees the player and shows its notification;
- two listens with a skip and a rewind teach a favorite, and a jump lands
  1.5 s before it, without counting as the listener's own seek;
- the notification's Pin button pins, and a jump still lands after the player
  leaves the active sessions (Spotify does that after a long pause);
- the screen draws at the phone's size, on a small screen and with large text
  (screenshots, to look at);
- nothing crashed;
- hookjump comes back after the system kills it, after a force stop once it is
  opened, and after a reboot.

Usage, from the repository root:

    cd android && ./gradlew :app:assembleDebug :testplayer:assembleDebug
    python3 android/emulators/check.py 26 30 37.0

It downloads each system image it lacks and, unless --keep is given, deletes
the image and the emulator afterwards: each takes several GB. Needs the
Android SDK (ANDROID_HOME, or ~/Library/Android/sdk) and Java for sdkmanager
(JAVA_HOME). Screenshots and a log go to android/build/emulator-check/.
"""

import argparse
import os
import platform
import re
import sqlite3
import subprocess
import sys
import time
from pathlib import Path

ANDROID = Path(__file__).resolve().parent.parent
SDK = Path(os.environ.get("ANDROID_HOME", Path.home() / "Library/Android/sdk"))
ADB = SDK / "platform-tools/adb"
EMULATOR = SDK / "emulator/emulator"
TOOLS = SDK / "cmdline-tools/latest/bin"
APPS = {
    "hookjump": ANDROID / "app/build/outputs/apk/debug/app-debug.apk",
    "test player": ANDROID / "testplayer/build/outputs/apk/debug/testplayer-debug.apk",
}
LISTENER = "app.hookjump/app.hookjump.Listener"
PLAYER = "app.hookjump.testplayer/.Player"
TAG = "hookjumpTestPlayer"
FIRST_SONG = "Çıpa Şarkısı"  # the test player's first song
PORT = 5580


class Emulator:
    """One emulator, reached through adb by its serial."""

    def __init__(self, api: str, out: Path) -> None:
        self.api = api
        self.name = f"hookjump-api{api}"
        self.serial = f"emulator-{PORT}"
        self.out = out
        self.process: subprocess.Popen | None = None

    def adb(self, *args: str, check: bool = False) -> str:
        done = subprocess.run(
            [str(ADB), "-s", self.serial, *args],
            capture_output=True,
            text=True,
            check=check,
        )
        return done.stdout + done.stderr

    def shell(self, command: str) -> str:
        return self.adb("shell", command)

    def boot(self) -> None:
        self.process = subprocess.Popen(
            [str(EMULATOR), "-avd", self.name, "-no-window", "-no-audio"]
            + ["-no-boot-anim", "-no-snapshot", "-gpu", "swiftshader_indirect"]
            + ["-port", str(PORT)],
            stdout=(self.out / "emulator.log").open("w"),
            stderr=subprocess.STDOUT,
        )
        self.wait_for_boot(timeout_s=300)

    def wait_for_boot(self, timeout_s: int) -> None:
        self.adb("wait-for-device")
        booted = until(
            lambda: self.shell("getprop sys.boot_completed").strip() == "1", timeout_s
        )
        if not booted:
            raise RuntimeError("the emulator did not finish booting")

    def root(self) -> None:
        """Root, so the notification's buttons can be pressed from adb."""
        self.adb("root")
        time.sleep(2)
        self.wait_for_boot(timeout_s=60)

    def shut_down(self) -> None:
        self.adb("emu", "kill")
        if self.process:
            try:
                self.process.wait(timeout=60)
            except subprocess.TimeoutExpired:
                self.process.kill()

    # The two apps ------------------------------------------------------------

    def player(self, action: str, **extras: int) -> None:
        flags = []
        for key, value in extras.items():
            flags += ["--el" if key == "at" else "--ei", key, str(value)]
        self.adb(
            "shell",
            "am",
            "start-foreground-service",
            "-n",
            PLAYER,
            "-a",
            action,
            *flags,
        )

    def press(self, button: str) -> None:
        """A button of hookjump's notification: JUMP or PIN."""
        self.shell(f"am broadcast -n app.hookjump/.Actions -a app.hookjump.{button}")

    def open_hookjump(self) -> None:
        """What the launcher icon does: jump while playing, else the screen."""
        self.shell("am start -n app.hookjump/.LaunchActivity")

    def bound(self) -> bool:
        return f"{{{LISTENER}}} (user 0)" in self.shell("dumpsys notification")

    def notice(self) -> str:
        """The text of hookjump's notification, as the system shows it."""
        dump = self.shell("dumpsys notification --noredact")
        start = dump.find("pkg=app.hookjump ")
        texts = re.findall(r"android\.text=String \((.*)\)", dump[start:])
        return texts[0] if start >= 0 and texts else ""

    def asked(self) -> list[str]:
        """What hookjump asked of the test player, in order."""
        log = self.adb("logcat", "-d", "-s", f"{TAG}:I")
        return re.findall(r"asked: (.*)", log)

    def screenshot(self, name: str) -> Path:
        path = self.out / f"{name}.png"
        shot = subprocess.run(
            [str(ADB), "-s", self.serial, "exec-out", "screencap", "-p"],
            capture_output=True,
            check=False,
        )
        path.write_bytes(shot.stdout)
        return path

    def screen(self, name: str) -> Path:
        """Open hookjump's screen (the player is paused) and take a picture."""
        self.shell("input keyevent HOME")
        self.open_hookjump()
        until(lambda: "app.hookjump/.MainActivity" in self.resumed(), 15)
        time.sleep(2)  # let the screen settle
        return self.screenshot(name)

    def resumed(self) -> str:
        dump = self.shell("dumpsys activity activities")
        return "\n".join(
            line for line in dump.splitlines() if "ResumedActivity" in line
        )


class Report:
    def __init__(self, api: str, out: Path) -> None:
        self.api = api
        self.failed = 0
        self.log = (out / "report.txt").open("w")

    def check(self, what: str, ok: bool, detail: str = "") -> bool:
        line = f"{'PASS' if ok else 'FAIL'}  {what}" + (f": {detail}" if detail else "")
        self.failed += not ok
        print(f"  [{self.api}] {line}", flush=True)
        self.log.write(line + "\n")
        return ok

    def note(self, text: str) -> None:
        print(f"  [{self.api}] note  {text}", flush=True)
        self.log.write(f"note  {text}\n")


def until(condition, timeout_s: float, every_s: float = 1.0) -> bool:
    deadline = time.monotonic() + timeout_s
    while time.monotonic() < deadline:
        if condition():
            return True
        time.sleep(every_s)
    return condition()


# The checks --------------------------------------------------------------------


def run_checks(phone: Emulator, report: Report) -> None:
    release = phone.shell("getprop ro.build.version.release").strip()
    report.note(f"Android {release}, API {phone.api}")
    for name, apk in APPS.items():
        out = phone.adb("install", "-r", "-g", str(apk))
        last = out.strip().splitlines()[-1] if out.strip() else "no answer"
        report.check(
            f"installs {name}", "Success" in out, "" if "Success" in out else last
        )
    allow(phone)
    report.check("notification access binds the listener", until(phone.bound, 30))

    phone.adb("logcat", "-c")
    phone.player("play", song=0, at=0)
    seen = until(lambda: "nothing pinned or learned" in phone.notice(), 20)
    report.check("hookjump sees the player and says so", seen, phone.notice())

    learn(phone)
    favorite = "Jumps to 1:43: the part you replay most."
    learned = until(lambda: phone.notice() == favorite, 10)
    report.check("two listens teach a favorite", learned, phone.notice())
    phone.open_hookjump()  # while playing: a jump
    landed = until(lambda: "seekTo 103500" in phone.asked(), 10)
    report.check("a jump lands 1.5 s before the favorite", landed, str(phone.asked()))
    seeks = stored_seeks(phone)
    expected = [(1, 130000), (1, 105000), (2, 130000), (2, 105000)]
    report.check(
        "only the listener's own seeks are learned",
        [(listen, to) for listen, _, to in seeks] == expected,
        str(seeks),
    )

    phone.root()
    phone.player("seek", at=72000)
    time.sleep(1)
    phone.press("PIN")
    pinned = until(lambda: "your pinned spot" in phone.notice(), 10)
    report.check("the notification's Pin pins", pinned, phone.notice())
    phone.player("pause")
    time.sleep(1)
    phone.player("hide")  # as Spotify after a long pause
    time.sleep(1)
    asked = len(phone.asked())
    phone.press("JUMP")
    jumped = until(lambda: len(phone.asked()) > asked, 10)
    report.check(
        "a jump lands after the player leaves the active sessions",
        jumped and phone.asked()[-1].startswith("seekTo"),
        str(phone.asked()[asked:]),
    )

    time.sleep(3)  # until the jump's toast is gone from the screen
    screens(phone, report)
    crashes = phone.adb("logcat", "-d", "-b", "crash")
    report.check(
        "nothing crashed",
        "app.hookjump" not in crashes,
        crashes.strip()[-400:],
    )
    lifecycle(phone, report)


def allow(phone: Emulator) -> None:
    """Notification access, as the listener gets it in Settings."""
    out = phone.shell(f"cmd notification allow_listener {LISTENER}")
    if "No shell command" in out or "Unknown command" in out:  # Android 8.0
        phone.shell(f"settings put secure enabled_notification_listeners {LISTENER}")


def learn(phone: Emulator) -> None:
    """Two listens of the first song, each with a skip to 2:10 and a rewind to
    1:45: a rewind and a skip in two listens are a favorite at 1:45."""
    for listen in (1, 2):
        if listen == 2:
            phone.player("play", song=0, at=0)
        time.sleep(3)
        phone.player("seek", at=130000)
        time.sleep(2)
        phone.player("seek", at=105000)
        time.sleep(2)
        phone.player("play", song=1, at=0)  # another song ends the listen
        time.sleep(2)
    phone.player("play", song=0, at=0)
    time.sleep(2)


def stored_seeks(phone: Emulator) -> list[tuple[int, int, int]]:
    query = "SELECT listen, from_ms, to_ms FROM seeks ORDER BY rowid"
    file = phone.out / "hookjump.db"
    copy = subprocess.run(
        [str(ADB), "-s", phone.serial, "exec-out", "run-as", "app.hookjump"]
        + ["cat", "databases/hookjump.db"],
        capture_output=True,
        check=False,
    )
    file.write_bytes(copy.stdout)
    with sqlite3.connect(f"file:{file}?mode=ro", uri=True) as db:
        return db.execute(query).fetchall()


def screens(phone: Emulator, report: Report) -> None:
    shots = [phone.screen("screen")]
    phone.shell("settings put system font_scale 1.3")
    shots.append(phone.screen("screen-large-text"))
    phone.shell("settings put system font_scale 1.0")
    phone.shell("wm size 720x1280")
    phone.shell("wm density 320")
    shots.append(phone.screen("screen-small"))
    phone.shell("wm size reset")
    phone.shell("wm density reset")
    ok = all(path.stat().st_size > 10_000 for path in shots)
    report.check("screenshots taken", ok, ", ".join(path.name for path in shots))


def running(phone: Emulator) -> bool:
    return bool(phone.shell("pidof app.hookjump").strip()) and phone.bound()


def lifecycle(phone: Emulator, report: Report) -> None:
    phone.shell("input keyevent HOME")
    pid = phone.shell("pidof app.hookjump").strip()
    phone.shell(f"kill -9 {pid}")
    back = until(
        lambda: (
            phone.shell("pidof app.hookjump").strip() not in ("", pid) and phone.bound()
        ),
        60,
    )
    report.check("back after the system kills it", back)

    # Versions differ here, so this is noted, not judged: what matters is
    # that opening hookjump brings it back.
    phone.shell("am force-stop app.hookjump")
    if not until(lambda: not running(phone), 10):
        report.note("a force stop left the listener running")
    elif until(lambda: running(phone), 30):
        report.note("after a force stop the listener came back by itself")
    else:
        report.note("after a force stop the listener stays off until opened")
    phone.open_hookjump()
    started = time.monotonic()
    back = until(phone.bound, 60)
    took = f"{time.monotonic() - started:.0f} s after opening hookjump"
    report.check("back once opened after a force stop", back, took)

    phone.adb("reboot")
    phone.wait_for_boot(timeout_s=300)
    report.check("back after a reboot", until(phone.bound, 120))


# Images and emulators --------------------------------------------------------------


ABI = "arm64-v8a" if platform.machine() in ("arm64", "aarch64") else "x86_64"


def image(api: str) -> str:
    return f"system-images;android-{api};default;{ABI}"


def image_folder(api: str) -> Path:
    return SDK / "system-images" / f"android-{api}" / "default" / ABI


def tool(name: str, *args: str, answer: str = "") -> str:
    done = subprocess.run(
        [str(TOOLS / name), *args],
        input=answer,
        capture_output=True,
        text=True,
        check=False,
    )
    return done.stdout + done.stderr


def prepare(api: str) -> None:
    if not (image_folder(api) / "package.xml").exists():
        print(f"  [{api}] downloading {image(api)}", flush=True)
        tool("sdkmanager", image(api), answer="y\n")
    tool(
        "avdmanager",
        "create", "avd", "--force", "-n", f"hookjump-api{api}",
        "-k", image(api), "-d", "pixel_5",
        answer="no\n",
    )  # fmt: skip


def clean_up(api: str) -> None:
    tool("avdmanager", "delete", "avd", "-n", f"hookjump-api{api}")
    tool("sdkmanager", "--uninstall", image(api))
    # sdkmanager leaves the image's folders behind, empty.
    for folder in (image_folder(api), *image_folder(api).parents[:2]):
        if folder.exists() and not any(folder.iterdir()):
            folder.rmdir()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("apis", nargs="+", help="API levels, such as 26 30 37.0")
    parser.add_argument(
        "--keep", action="store_true", help="keep the images and emulators"
    )
    args = parser.parse_args()
    missing = [str(apk) for apk in APPS.values() if not apk.exists()]
    if missing:
        sys.exit("Build the apps first: " + ", ".join(missing))

    failed = 0
    for api in args.apis:
        out = ANDROID / "build" / "emulator-check" / f"api{api}"
        out.mkdir(parents=True, exist_ok=True)
        report = Report(api, out)
        phone = Emulator(api, out)
        try:
            prepare(api)
            phone.boot()
            run_checks(phone, report)
        except Exception as error:  # noqa: BLE001  report it, go on to the next
            report.check("the run finished", False, repr(error))
        finally:
            phone.shut_down()
            if not args.keep:
                clean_up(api)
        failed += report.failed
        print(f"  [{api}] {report.failed} failed; screenshots in {out}", flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
