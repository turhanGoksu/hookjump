"""hookjump: jump to the best part of the song that is playing.

The best part comes from repeated lines in time-synced lyrics (the chorus) and,
over time, from the listener's own rewinds and replays, recorded locally.
"""

from importlib.metadata import PackageNotFoundError, version

try:
    __version__ = version("hookjump")
except PackageNotFoundError:  # running from a source tree without installing
    __version__ = "0+unknown"
