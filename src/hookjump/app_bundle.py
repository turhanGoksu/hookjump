"""hookjump.app: lets hookjump open at login and ask permission in its own name.

``hookjump install`` builds it on this Mac: an Info.plist, the hook as its
icon, and a small native launcher compiled from launcher.c, which starts the
Python app as its child. macOS then sees "hookjump" asking to control Spotify,
not "Python", so no other Python program gets that permission with it.

The app's contents stay the same from one hookjump version to the next: they
are ad-hoc signed, and a change would make macOS ask for the permission again.
"""

import plistlib
import shutil
import subprocess
import tempfile
from collections.abc import Callable, Sequence
from importlib.resources import files
from pathlib import Path

BUNDLE_ID = "app.hookjump"
APP = Path.home() / "Applications" / "hookjump.app"
_LSREGISTER = (
    "/System/Library/Frameworks/CoreServices.framework/Frameworks/"
    "LaunchServices.framework/Support/lsregister"
)
_PROTECTED = ("Desktop", "Documents", "Downloads")  # macOS asks before access
_ICON_SIZES = (16, 32, 128, 256, 512)

Run = Callable[[Sequence[str]], subprocess.CompletedProcess]


class InstallError(RuntimeError):
    pass


def _run(command: Sequence[str]) -> subprocess.CompletedProcess:
    return subprocess.run(command, capture_output=True, text=True, check=False)


def info_plist() -> dict:
    return {
        "CFBundleIdentifier": BUNDLE_ID,
        "CFBundleName": "hookjump",
        "CFBundleExecutable": "hookjump",
        "CFBundleIconFile": "hookjump",
        "CFBundlePackageType": "APPL",
        "LSUIElement": True,  # menu bar only: no Dock icon
        "LSMinimumSystemVersion": "13.0",
        "NSAppleEventsUsageDescription": (
            "hookjump reads which song is playing in Spotify and moves it to"
            " the best part."
        ),
    }


def protected_folder(python: Path, home: Path | None = None) -> str | None:
    """The protected folder ``python`` lives in, if any (Desktop, ...)."""
    home = home or Path.home()
    for name in _PROTECTED:
        if python.is_relative_to(home / name):
            return name
    return None


def install(
    python: Path,
    app: Path = APP,
    run: Run = _run,
    icon: Callable[[Path, Run], None] | None = None,
) -> None:
    """Build hookjump.app for ``python`` (the one hookjump is installed in)."""
    # /usr/bin/clang is there even without the tools, and would only offer to
    # install them; xcode-select knows whether they are installed.
    if run(["xcode-select", "-p"]).returncode != 0:
        raise InstallError(
            "Building hookjump.app needs Apple's Command Line Tools (once). Install"
            " them with: xcode-select --install. Until then, hookjump works from a"
            " terminal: hookjump run"
        )
    source = files("hookjump") / "launcher" / "launcher.c"
    with tempfile.TemporaryDirectory() as temp:
        built = Path(temp) / "hookjump.app"
        macos, resources = (
            built / "Contents" / "MacOS",
            built / "Contents" / "Resources",
        )
        macos.mkdir(parents=True)
        resources.mkdir()
        (built / "Contents" / "Info.plist").write_bytes(plistlib.dumps(info_plist()))
        (resources / "python-path").write_text(f"{python}\n", encoding="utf-8")
        _check(
            run([
                "clang", "-O2", "-o", str(macos / "hookjump"), str(source),
                "-lobjc", "-framework", "Foundation", "-framework", "ServiceManagement",
            ]),
            "compiling the launcher",
        )  # fmt: skip
        if icon is not None:
            icon(resources / "hookjump.icns", run)
        _check(
            run(["codesign", "--force", "--sign", "-", "--identifier", BUNDLE_ID,
                 str(built)]),
            "signing hookjump.app",
        )  # fmt: skip
        if app.exists():
            shutil.rmtree(app)
        app.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(built), str(app))
    run([_LSREGISTER, "-f", str(app)])


def uninstall(app: Path = APP, run: Run = _run) -> None:
    """Remove hookjump.app, its login item and the permissions it was given."""
    if not app.exists():
        raise InstallError(f"hookjump.app is not installed in {app.parent}")
    run([str(app / "Contents" / "MacOS" / "hookjump"), "--login", "off"])
    # Permissions can only be reset while macOS still knows the app.
    run(["tccutil", "reset", "All", BUNDLE_ID])
    run([_LSREGISTER, "-u", str(app)])
    shutil.rmtree(app)


def open_at_login(on: bool, app: Path = APP, run: Run = _run) -> None:
    launcher = str(app / "Contents" / "MacOS" / "hookjump")
    _check(run([launcher, "--login", "on" if on else "off"]), "changing open at login")


def opens_at_login(app: Path = APP, run: Run = _run) -> bool:
    result = run([str(app / "Contents" / "MacOS" / "hookjump"), "--login", "status"])
    return result.returncode == 0 and result.stdout.strip() == "1"


def write_icon(path: Path, run: Run) -> None:
    """The hook, as an .icns, from PNGs drawn at every size macOS uses."""
    from hookjump.icon import app_icon_png  # AppKit, so only when building

    with tempfile.TemporaryDirectory() as temp:
        iconset = Path(temp) / "hookjump.iconset"
        iconset.mkdir()
        for size in _ICON_SIZES:
            (iconset / f"icon_{size}x{size}.png").write_bytes(app_icon_png(size))
            (iconset / f"icon_{size}x{size}@2x.png").write_bytes(app_icon_png(size * 2))
        _check(run(["iconutil", "-c", "icns", str(iconset), "-o", str(path)]),
               "making the icon")  # fmt: skip


def _check(result: subprocess.CompletedProcess, doing: str) -> None:
    if result.returncode != 0:
        raise InstallError(f"Failed {doing}: {(result.stderr or '').strip()}")
