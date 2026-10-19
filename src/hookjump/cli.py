"""The ``hookjump`` command: start the app, or knock on it (door 2)."""

import argparse
import sys
from collections.abc import Callable, Sequence
from pathlib import Path

from hookjump import __version__
from hookjump.words import describe_jump, describe_pin


def main(
    argv: Sequence[str] | None = None,
    knock: Callable[[str], dict | None] | None = None,
) -> int:
    parser = argparse.ArgumentParser(
        prog="hookjump",
        description="Jump to the best part of the song playing in Spotify.",
    )
    parser.add_argument(
        "--version", action="version", version=f"hookjump {__version__}"
    )
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("run", help="start the menu bar app")
    commands.add_parser("jump", help="jump to the best part of the playing song")
    commands.add_parser("pin", help="pin where the playing song is now")
    commands.add_parser(
        "install", help="build hookjump.app, so hookjump can open at login"
    )
    commands.add_parser(
        "uninstall", help="remove hookjump.app, its login item and permissions"
    )
    args = parser.parse_args(argv)

    # PyPI lets anyone install it anywhere; say so instead of a stack trace.
    if sys.platform != "darwin":
        print("hookjump runs on macOS only, for now.", file=sys.stderr)
        return 1
    if args.command == "run":
        from hookjump.menubar import run

        return run()

    if knock is None:
        from hookjump.door import knock
    if args.command in ("install", "uninstall"):
        return _app(args.command, knock)
    reply = knock(args.command)
    if reply is None:
        print("hookjump is not running. Start it with: hookjump run", file=sys.stderr)
        return 1
    if args.command == "jump":
        status = reply.get("status", "player_unavailable")
        print(describe_jump(status, reply.get("target_ms"), reply.get("landing")))
        return 0 if reply.get("landing") == "landed" else 1
    print(describe_pin(reply.get("pinned_ms")))
    return 0 if reply.get("pinned_ms") is not None else 1


def _app(command: str, knock: Callable[[str], dict | None]) -> int:
    from hookjump import app_bundle

    try:
        if command == "uninstall":
            app_bundle.uninstall()
            print("hookjump.app, its login item and its permissions are gone.")
            return 0
        python = Path(sys.executable)
        if folder := app_bundle.protected_folder(python):
            print(
                f"Note: this hookjump lives in your {folder} folder, so macOS will"
                " also ask hookjump.app for access to it. Installing with pipx"
                " avoids that.",
                file=sys.stderr,
            )
        app_bundle.install(python, icon=app_bundle.write_icon)
    except app_bundle.InstallError as error:
        print(error, file=sys.stderr)
        return 1
    print(f"Installed {app_bundle.APP}")
    if knock("ping") is not None:
        print("hookjump is running from a terminal: quit it first (Ctrl+C there).")
    print(f"Start it with: open {app_bundle.APP}")
    print("Then choose Open at Login in its menu to start it with your Mac.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
