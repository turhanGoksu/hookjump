package app.hookjump.booth

import app.hookjump.core.Favorite
import app.hookjump.core.Pin
import app.hookjump.core.SeekEvent
import app.hookjump.player.Reading
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

private const val T0 = 1_700_000_000_000L
private val READING = Reading(T0, "a", "Alışamadım", "Ezhel", 153268, true, 40000)
private val SEEKS = listOf(SeekEvent("a", 1, 130000, 105000), SeekEvent("a", 2, 10000, 60000))

/** The chart, as data: the same cases as tests/test_sleeve.py on the Mac. */
class ChartTest {
    @Test
    fun `a pin is the hook`() {
        val chart = chartFor(READING, Pin(72000, typed = false), null, SEEKS)!!
        assertEquals(Triple("Alışamadım", "Ezhel", 72000L), Triple(chart.title, chart.artist, chart.hookMs))
        assertEquals("Your pinned spot" to "the jump lands 1.5 s before it", chart.hookLabel to chart.hookDetail)
        assertEquals(listOf(105000L) to listOf(60000L), chart.rewinds to chart.skips)
        assertEquals(true, chart.pinned)
        assertEquals(false, chartFor(READING, null, Favorite(105000, 6, 2), SEEKS)!!.pinned)
    }

    @Test
    fun `a typed pin says so`() {
        assertEquals("the jump lands right there", chartFor(READING, Pin(83000, typed = true), null, emptyList())!!.hookDetail)
    }

    @Test
    fun `a learned favorite says how it was learned`() {
        val chart = chartFor(READING, null, Favorite(105000, 6, 2), SEEKS)!!
        assertEquals(Triple(105000L, "The part you replay most", "learned from 2 listens"), Triple(chart.hookMs, chart.hookLabel, chart.hookDetail))
    }

    @Test
    fun `nothing known says what to do`() {
        val chart = chartFor(READING, null, null, emptyList())!!
        assertNull(chart.hookMs)
        assertEquals("Nothing learned yet", chart.hookLabel)
    }

    @Test
    fun `with no hook it says why nothing is learned`() {
        val ignored = chartFor(READING, null, null, emptyList(), ignored = true)!!
        assertEquals("Not learning from this song" to "a pin still works", ignored.hookLabel to ignored.hookDetail)
        val paused = chartFor(READING, null, null, emptyList(), learningPaused = true)!!
        assertEquals("Learning is paused" to "a pin still works", paused.hookLabel to paused.hookDetail)
        // A pin is still the hook, whatever is learned or not.
        assertEquals("Your pinned spot", chartFor(READING, Pin(1000, false), null, emptyList(), ignored = true, learningPaused = true)!!.hookLabel)
    }

    @Test
    fun `no chart without a track to draw`() {
        assertNull(chartFor(null, null, null, emptyList()))
        assertNull(chartFor(READING.copy(trackId = null), null, null, emptyList()))
        assertNull(chartFor(READING.copy(durationMs = null), null, null, emptyList())) // no length
    }

    @Test
    fun `a missing title or artist`() {
        val noTitle = chartFor(READING.copy(title = null), null, null, emptyList())!!
        assertEquals("Ezhel" to null, noTitle.title to noTitle.artist)
        assertEquals("Unknown track", chartFor(READING.copy(title = null, artist = null), null, null, emptyList())!!.title)
    }

    @Test
    fun `the float moves on while playing, and stops at the end`() {
        val chart = chartFor(READING, null, null, emptyList())!!
        assertEquals(45000L, chart.nowAt(T0 + 5000))
        assertEquals(153268L, chart.nowAt(T0 + 600000))
        assertEquals(40000L, chartFor(READING.copy(playing = false), null, null, emptyList())!!.nowAt(T0 + 5000))
    }

    @Test
    fun `fraction and back`() {
        assertEquals(0.5, fraction(76634, 153268))
        assertEquals(0.0 to 1.0, fraction(-5, 153268) to fraction(999999, 153268))
        assertEquals(77000L, msAt(0.5, 153268)) // whole seconds
        assertEquals(153000L, msAt(1.2, 153268)) // the end, to the whole second
        assertEquals(153600L, msAt(1.0, 153600)) // rounding up never passes the end
        assertEquals(0L, msAt(-0.1, 153268))
    }
}
