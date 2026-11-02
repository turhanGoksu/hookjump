package app.hookjump.player

import app.hookjump.core.PlaybackSample

/**
 * Turns the player's reports into the samples seek detection reads.
 *
 * Seek detection (spec/ALGORITHM.md, section 3) expects samples about a second
 * apart. Android tells about changes instead, so for each new report the ear
 * writes two samples: one just before it, where playback would have been had
 * nothing happened, and the report itself. A seek then shows up between the
 * two, at its exact moment; untouched playback shows up as nothing.
 */
class Ear {
    private var last: Reading? = null
    private var silent = false // the last thing heard was "no player"
    private var ownMove = false

    /** hookjump moved the playhead: mark the next sample as its own. */
    fun heardOwnMove() {
        ownMove = true
    }

    /** Samples for a report, or none if it says nothing new. [nowMs] dates "no player". */
    fun hear(reading: Reading?, nowMs: Long): List<PlaybackSample> {
        if (reading == null) {
            if (silent) return emptyList()
            silent = true
            last = null
            return listOf(PlaybackSample(nowMs, null, false, null, takeOwnMove()))
        }
        silent = false
        val prev = last
        if (reading == prev) return emptyList()
        last = reading

        val samples = mutableListOf<PlaybackSample>()
        val justBefore = reading.atMs - 1
        if (prev != null && prev.trackId != null && prev.trackId == reading.trackId &&
            prev.positionMs != null && justBefore > prev.atMs
        ) {
            val expected = if (prev.playing) prev.positionMs + (justBefore - prev.atMs) else prev.positionMs
            samples += PlaybackSample(justBefore, prev.trackId, prev.playing, expected)
        }
        samples += PlaybackSample(reading.atMs, reading.trackId, reading.playing, reading.positionMs, takeOwnMove())
        return samples
    }

    private fun takeOwnMove(): Boolean = ownMove.also { ownMove = false }
}
