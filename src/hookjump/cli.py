"""The ``hookjump`` command: start the app, or knock on it (door 2)."""

import argparse
import sys
from collections.abc import Callable, Sequence

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
    args = parser.parse_args(argv)

    if args.command == "run":
        from hookjump.menubar import run

        return run()

    if knock is None:
        from hookjump.door import knock
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


if __name__ == "__main__":
    sys.exit(main())
