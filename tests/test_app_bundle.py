"""hookjump.app: what install and uninstall do, with the commands faked.

One test compiles the real launcher; it runs on macOS with Apple's tools only.
"""

import plistlib
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from hookjump import app_bundle
from hookjump.app_bundle import InstallError, install, uninstall

PYTHON = Path("/Users/someone/.local/pipx/venvs/hookjump/bin/python")


class FakeRun:
    """Records commands; fails the ones named in ``fail``."""

    def __init__(self, fail: str = "", stdout: str = "") -> None:
        self.commands: list[list[str]] = []
        self.fail, self.stdout = fail, stdout

    def __call__(self, command):
        self.commands.append([str(part) for part in command])
        failed = bool(self.fail) and self.fail in " ".join(map(str, command))
        return subprocess.CompletedProcess(
            command, 1 if failed else 0, self.stdout, "boom" if failed else ""
        )

    def tools(self) -> list[str]:
        return [Path(c[0]).name for c in self.commands]


def test_the_app_identity_never_changes_between_versions() -> None:
    plist = app_bundle.info_plist()
    assert plist["CFBundleIdentifier"] == "app.hookjump"
    assert plist["LSUIElement"] is True
    assert "Spotify" in plist["NSAppleEventsUsageDescription"]
    # A version in it would change the signature at every upgrade, and macOS
    # would ask for the Spotify permission again.
    assert "CFBundleVersion" not in plist
    assert "CFBundleShortVersionString" not in plist


@pytest.mark.parametrize(
    ("python", "folder"),
    [
        ("/Users/u/Desktop/hookjump/.venv/bin/python", "Desktop"),
        ("/Users/u/Documents/x/bin/python", "Documents"),
        ("/Users/u/.local/pipx/venvs/hookjump/bin/python", None),
        ("/opt/homebrew/bin/python3", None),
    ],
)
def test_protected_folders(python: str, folder: str | None) -> None:
    assert app_bundle.protected_folder(Path(python), Path("/Users/u")) == folder


def test_install_builds_signs_and_registers(tmp_path: Path) -> None:
    run, app = FakeRun(), tmp_path / "Applications" / "hookjump.app"
    install(PYTHON, app, run)
    assert run.tools() == ["xcode-select", "clang", "codesign", "lsregister"]
    clang = run.commands[1]
    assert clang[clang.index("-o") + 1].endswith("hookjump.app/Contents/MacOS/hookjump")
    assert any(part.endswith("launcher/launcher.c") for part in clang)
    assert "app.hookjump" in run.commands[2]  # signed with the bundle identifier
    contents = app / "Contents"
    assert (
        plistlib.loads((contents / "Info.plist").read_bytes())
        == app_bundle.info_plist()
    )
    assert (contents / "Resources" / "python-path").read_text() == f"{PYTHON}\n"


def test_install_twice_gives_the_same_contents(tmp_path: Path) -> None:
    app = tmp_path / "hookjump.app"
    install(PYTHON, app, FakeRun())
    first = {p.relative_to(app): p.read_bytes() for p in app.rglob("*") if p.is_file()}
    install(PYTHON, app, FakeRun())
    again = {p.relative_to(app): p.read_bytes() for p in app.rglob("*") if p.is_file()}
    assert first == again


def test_install_replaces_an_older_app(tmp_path: Path) -> None:
    app = tmp_path / "hookjump.app"
    (app / "Contents" / "old").mkdir(parents=True)
    install(PYTHON, app, FakeRun())
    assert not (app / "Contents" / "old").exists()


def test_without_the_command_line_tools_nothing_is_built(tmp_path: Path) -> None:
    run, app = FakeRun(fail="xcode-select"), tmp_path / "hookjump.app"
    with pytest.raises(InstallError, match="xcode-select --install"):
        install(PYTHON, app, run)
    assert run.tools() == ["xcode-select"] and not app.exists()


def test_a_failed_compile_says_why_and_leaves_no_app(tmp_path: Path) -> None:
    app = tmp_path / "hookjump.app"
    with pytest.raises(InstallError, match="compiling the launcher: boom"):
        install(PYTHON, app, FakeRun(fail="clang"))
    assert not app.exists()


def test_uninstall_resets_permissions_before_removing(tmp_path: Path) -> None:
    app = tmp_path / "hookjump.app"
    install(PYTHON, app, FakeRun())
    run = FakeRun()
    uninstall(app, run)
    assert run.tools() == ["hookjump", "tccutil", "lsregister"]
    assert run.commands[0][1:] == ["--login", "off"]
    assert run.commands[1] == ["tccutil", "reset", "All", "app.hookjump"]
    assert not app.exists()


def test_uninstall_when_not_installed(tmp_path: Path) -> None:
    with pytest.raises(InstallError, match="not installed"):
        uninstall(tmp_path / "hookjump.app", FakeRun())


def test_open_at_login_status(tmp_path: Path) -> None:
    assert app_bundle.opens_at_login(tmp_path, FakeRun(stdout="1\n"))
    assert not app_bundle.opens_at_login(tmp_path, FakeRun(stdout="0\n"))


@pytest.mark.skipif(
    sys.platform != "darwin" or shutil.which("xcode-select") is None,
    reason="compiles with Apple's tools",
)
def test_the_real_launcher_compiles_and_answers(tmp_path: Path) -> None:
    if subprocess.run(["xcode-select", "-p"], capture_output=True).returncode:
        pytest.skip("no Command Line Tools")
    app = tmp_path / "hookjump.app"
    install(PYTHON, app)  # real clang and codesign; lsregister on a temp path
    launcher = str(app / "Contents" / "MacOS" / "hookjump")
    status = subprocess.run(
        [launcher, "--login", "status"], capture_output=True, text=True
    )
    assert status.returncode == 0 and status.stdout.strip() in {"0", "1", "2", "3"}
    wrong = subprocess.run(
        [launcher, "--login", "maybe"], capture_output=True, text=True
    )
    assert wrong.returncode == 2 and "on, off or status" in wrong.stderr
    signature = subprocess.run(
        ["codesign", "-dv", str(app)], capture_output=True, text=True
    ).stderr
    assert "Identifier=app.hookjump" in signature
    subprocess.run([app_bundle._LSREGISTER, "-u", str(app)], capture_output=True)
