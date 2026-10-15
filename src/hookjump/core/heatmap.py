"""Find the listener's favorite part of a track from their seeks.

spec/ALGORITHM.md, section 6. Untouched playback says nothing about taste, so
only the listener's own moves count: where they rewound to, and where they
skipped forward to.
"""

from collections.abc import Sequence

from hookjump.core.models import Favorite, SeekEvent

REWIND_POINTS = 3  # "I want to hear this again": the strongest signal
FORWARD_POINTS = 2  # "I want to get here", but also just looking around
REGION_GAP_MS = 5000  # arrivals closer than this are the same place
MIN_POINTS = 5
MIN_LISTENS = 2  # one stray seek, or many in one sitting, is not a habit
# Only the listens in which the listener seeked count, the most recent ones
# first. Listens without seeks (untouched, or jumped by hookjump) never push old
# seeks out: otherwise a learned favorite would fade exactly because the hotkey
# now does the seeking. New seeks do, so a change of taste is followed.
RECENT_LISTENS = 10


def favorite(seeks: Sequence[SeekEvent]) -> Favorite | None:
    """The favorite part from all seeks of one track, or None if unsure yet.

    Listen numbers must grow with time: a later listen has a higher number.
    """
    if len({s.track_id for s in seeks}) > 1:
        raise ValueError("favorite() takes the seeks of one track")
    recent = set(sorted({s.listen for s in seeks})[-RECENT_LISTENS:])

    # Group arrival points into regions: a gap of more than REGION_GAP_MS
    # between neighbours starts a new region.
    regions: list[list[SeekEvent]] = []
    kept = (s for s in seeks if s.listen in recent)
    for seek in sorted(kept, key=lambda s: s.to_ms):
        if regions and seek.to_ms - regions[-1][-1].to_ms <= REGION_GAP_MS:
            regions[-1].append(seek)
        else:
            regions.append([seek])

    best: Favorite | None = None
    for region in regions:  # in time order: on a full tie the earlier one stays
        points = sum(
            REWIND_POINTS if s.to_ms < s.from_ms else FORWARD_POINTS for s in region
        )
        listens = len({s.listen for s in region})
        if points < MIN_POINTS or listens < MIN_LISTENS:
            continue
        if best is None or (points, listens) > (best.points, best.listens):
            # The lower median: always a point the listener actually chose.
            target = region[(len(region) - 1) // 2].to_ms
            best = Favorite(target, points, listens)
    return best
