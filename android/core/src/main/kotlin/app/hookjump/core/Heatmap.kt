package app.hookjump.core

// Find the listener's favorite part of a track from their seeks.
//
// spec/ALGORITHM.md, section 4. Untouched playback says nothing about taste, so
// only the listener's own moves count: where they rewound to, and where they
// skipped forward to.

const val REWIND_POINTS = 3 // "I want to hear this again": the strongest signal
const val FORWARD_POINTS = 2 // "I want to get here", but also just looking around
const val REGION_GAP_MS = 5000L // arrivals closer than this are the same place
const val MIN_POINTS = 5
const val MIN_LISTENS = 2 // one stray seek, or many in one sitting, is not a habit

// Only the listens in which the listener seeked count, the most recent ones
// first. Listens without seeks (untouched, or jumped by hookjump) never push old
// seeks out: otherwise a learned favorite would fade exactly because the button
// now does the seeking. New seeks do, so a change of taste is followed.
const val RECENT_LISTENS = 10

/**
 * The favorite part from all seeks of one track, or null if unsure yet.
 *
 * Listen numbers must grow with time: a later listen has a higher number.
 */
fun favorite(seeks: List<SeekEvent>): Favorite? {
    require(seeks.map { it.trackId }.toSet().size <= 1) { "favorite() takes the seeks of one track" }
    val recent = seeks.map { it.listen }.toSortedSet().toList().takeLast(RECENT_LISTENS).toSet()

    // Group arrival points into regions: a gap of more than REGION_GAP_MS
    // between neighbours starts a new region. sortedBy is stable, as the spec needs.
    val regions = mutableListOf<MutableList<SeekEvent>>()
    for (seek in seeks.filter { it.listen in recent }.sortedBy { it.toMs }) {
        val region = regions.lastOrNull()
        if (region != null && seek.toMs - region.last().toMs <= REGION_GAP_MS) {
            region += seek
        } else {
            regions += mutableListOf(seek)
        }
    }

    var best: Favorite? = null
    for (region in regions) { // in time order: on a full tie the earlier one stays
        val points = region.sumOf { if (it.toMs < it.fromMs) REWIND_POINTS else FORWARD_POINTS }
        val listens = region.map { it.listen }.toSet().size
        if (points < MIN_POINTS || listens < MIN_LISTENS) continue
        val current = best
        if (current == null || points > current.points ||
            (points == current.points && listens > current.listens)
        ) {
            // The lower median: always a point the listener actually chose.
            best = Favorite(region[(region.size - 1) / 2].toMs, points, listens)
        }
    }
    return best
}
