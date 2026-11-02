package app.hookjump.player

import app.hookjump.core.PlaybackSample
import app.hookjump.core.SeekEvent
import app.hookjump.core.detect
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

private const val T0 = 1_700_000_000_000L

private fun reading(atMs: Long, positionMs: Long?, track: String? = "a", playing: Boolean = true) =
    Reading(atMs, track, "Title", "Artist", 240000, playing, positionMs)

class EarTest {
    private val ear = Ear()

    @Test
    fun `a first report becomes one sample`() {
        assertEquals(listOf(PlaybackSample(T0, "a", true, 30000)), ear.hear(reading(T0, 30000), T0))
    }

    @Test
    fun `a report heard again adds nothing`() {
        ear.hear(reading(T0, 30000), T0)
        assertEquals(emptyList<PlaybackSample>(), ear.hear(reading(T0, 30000), T0 + 30000))
    }

    @Test
    fun `a later report gets a sample just before it, where playback would have been`() {
        ear.hear(reading(T0, 30000), T0)
        assertEquals(
            listOf(PlaybackSample(T0 + 9999, "a", true, 39999), PlaybackSample(T0 + 10000, "a", true, 40000)),
            ear.hear(reading(T0 + 10000, 40000), T0 + 10000),
        )
    }

    @Test
    fun `while paused the position stays`() {
        ear.hear(reading(T0, 30000, playing = false), T0)
        assertEquals(
            PlaybackSample(T0 + 59999, "a", false, 30000),
            ear.hear(reading(T0 + 60000, 30000, playing = true), T0 + 60000).first(),
        )
    }

    @Test
    fun `a new track has no sample before it`() {
        ear.hear(reading(T0, 30000), T0)
        assertEquals(listOf(PlaybackSample(T0 + 5000, "b", true, 0)), ear.hear(reading(T0 + 5000, 0, "b"), T0 + 5000))
    }

    @Test
    fun `a closed player is heard once`() {
        ear.hear(reading(T0, 30000), T0)
        assertEquals(listOf(PlaybackSample(T0 + 1, null, false, null)), ear.hear(null, T0 + 1))
        assertEquals(emptyList<PlaybackSample>(), ear.hear(null, T0 + 2))
    }

    @Test
    fun `a rewind reported minutes later is still found at its exact moment`() {
        // Played from 0:30 for 70 s, then rewound to 1:00: the report, even one
        // picked up by the 30 s check, says when it happened.
        val samples = ear.hear(reading(T0, 30000), T0) + ear.hear(reading(T0 + 70000, 60000), T0 + 95000)
        assertEquals(listOf(SeekEvent("a", 1, 99999, 60000)), detect(samples).filterIsInstance<SeekEvent>())
    }

    @Test
    fun `untouched playback reported now and then is no seek`() {
        var samples = ear.hear(reading(T0, 30000), T0)
        for (i in 1..10) samples = samples + ear.hear(reading(T0 + i * 30000, 30000 + i * 30000L + 300), T0 + i * 30000)
        assertEquals(emptyList<SeekEvent>(), detect(samples).filterIsInstance<SeekEvent>())
    }

    @Test
    fun `hookjump's own move is marked on the next sample only`() {
        ear.hear(reading(T0, 30000), T0)
        ear.heardOwnMove()
        assertEquals(emptyList<PlaybackSample>(), ear.hear(reading(T0, 30000), T0)) // nothing new: the mark waits
        val moved = ear.hear(reading(T0 + 1000, 100000), T0 + 1000)
        assertEquals(listOf(false, true), moved.map { it.ownSeek })
        assertEquals(false, ear.hear(reading(T0 + 2000, 101000), T0 + 2000).last().ownSeek)
        assertEquals(emptyList<SeekEvent>(), detect(listOf(PlaybackSample(T0, "a", true, 30000)) + moved).filterIsInstance<SeekEvent>())
    }
}
