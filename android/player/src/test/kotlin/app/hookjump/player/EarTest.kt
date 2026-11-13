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
    private val none = emptyList<PlaybackSample>()

    private fun seeks(samples: List<PlaybackSample>) = detect(samples).filterIsInstance<SeekEvent>()

    @Test
    fun `a first report becomes one sample`() {
        assertEquals(listOf(PlaybackSample(T0, "a", true, 30000)), ear.hear(reading(T0, 30000), T0))
    }

    @Test
    fun `a report heard again adds nothing`() {
        ear.hear(reading(T0, 30000), T0)
        assertEquals(none, ear.hear(reading(T0, 30000), T0 + 30000))
    }

    @Test
    fun `a report that matches playback adds nothing`() {
        // Spotify reports many times a second as a track starts.
        ear.hear(reading(T0, 90), T0)
        assertEquals(none, ear.hear(reading(T0 + 430, 520), T0 + 430))
        assertEquals(none, ear.hear(reading(T0 + 910, 1000 + 200), T0 + 910))
        assertEquals(none, ear.hear(reading(T0 + 60000, 60090, playing = true), T0 + 60000))
    }

    @Test
    fun `a report off from playback gets a sample just before it, where playback would have been`() {
        ear.hear(reading(T0, 30000), T0)
        ear.hear(reading(T0 + 430, 30430), T0 + 430) // quiet, but the newest base
        assertEquals(
            listOf(PlaybackSample(T0 + 9999, "a", true, 39999), PlaybackSample(T0 + 10000, "a", true, 45000)),
            ear.hear(reading(T0 + 10000, 45000), T0 + 10000),
        )
    }

    @Test
    fun `a track that has not played yet is not trusted`() {
        // Spotify starting: the last track loads at 0:00, then where it was
        // left, both paused. That is no seek.
        assertEquals(none, ear.hear(reading(T0, 0, playing = false), T0))
        assertEquals(none, ear.hear(reading(T0 + 1600, 102240, playing = false), T0 + 1600))
        val played = ear.hear(reading(T0 + 9000, 102240), T0 + 9000)
        assertEquals(listOf(PlaybackSample(T0 + 9000, "a", true, 102240)), played)
        assertEquals(emptyList<SeekEvent>(), seeks(played))
    }

    @Test
    fun `while paused the position stays`() {
        ear.hear(reading(T0, 30000), T0)
        ear.hear(reading(T0 + 1000, 31000, playing = false), T0 + 1000)
        assertEquals(none, ear.hear(reading(T0 + 20000, 31000, playing = false), T0 + 20000))
        assertEquals(
            PlaybackSample(T0 + 60999, "a", false, 31000),
            ear.hear(reading(T0 + 61000, 31000), T0 + 61000).first(),
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
        assertEquals(none, ear.hear(null, T0 + 2))
    }

    @Test
    fun `a player with nothing loaded is heard once`() {
        assertEquals(1, ear.hear(reading(T0, null, track = null, playing = false), T0).size)
        assertEquals(none, ear.hear(reading(T0 + 500, null, track = null, playing = false), T0 + 500))
    }

    @Test
    fun `a rewind reported minutes later is still found at its exact moment`() {
        // Played from 0:30 for 70 s, then rewound to 1:00: the report, even one
        // picked up by the 30 s check, says when it happened.
        val samples = ear.hear(reading(T0, 30000), T0) + ear.hear(reading(T0 + 70000, 60000), T0 + 95000)
        assertEquals(listOf(SeekEvent("a", 1, 99999, 60000)), seeks(samples))
    }

    @Test
    fun `a report said before the last one heard adds nothing`() {
        // The 30 s check read the player while older reports still waited.
        ear.hear(reading(T0, 30000), T0)
        ear.hear(reading(T0 + 5000, 100000), T0 + 5000)
        assertEquals(none, ear.hear(reading(T0 + 3000, 60000), T0 + 5001))
    }

    @Test
    fun `untouched playback reported now and then is no seek`() {
        var samples = ear.hear(reading(T0, 30000), T0)
        for (i in 1..10) samples = samples + ear.hear(reading(T0 + i * 30000, 30000 + i * 30000L + 300), T0 + i * 30000)
        assertEquals(emptyList<SeekEvent>(), seeks(samples))
    }

    @Test
    fun `hookjump's own move is marked on the next sample only`() {
        val first = ear.hear(reading(T0, 30000), T0)
        ear.heardOwnMove()
        assertEquals(none, ear.hear(reading(T0, 30000), T0)) // nothing new: the mark waits
        val moved = ear.hear(reading(T0 + 1000, 100000), T0 + 1000)
        assertEquals(listOf(false, true), moved.map { it.ownSeek })
        assertEquals(false, ear.hear(reading(T0 + 2000, 101500), T0 + 2000).last().ownSeek)
        assertEquals(emptyList<SeekEvent>(), seeks(first + moved))
    }
}
