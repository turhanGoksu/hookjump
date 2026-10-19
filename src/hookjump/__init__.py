"""hookjump: jump to the best part of the song that is playing.

The best part is the spot the listener pinned or, over time, the part they
keep going back to, learned from their own rewinds and skips on their Mac.
"""

from importlib.metadata import PackageNotFoundError, version

try:
    __version__ = version("hookjump")
except PackageNotFoundError:  # running from a source tree without installing
    __version__ = "0+unknown"
