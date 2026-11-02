package app.hookjump.booth

import app.hookjump.core.JumpResult
import app.hookjump.core.Status
import app.hookjump.player.Landing
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** The same sentences as tests/test_words.py checks on the Mac. */
class WordsTest {
    @Test
    fun clock() {
        assertEquals(listOf("0:00", "1:43", "0:59", "10:00"), listOf(0L, 103500, 59999, 600000).map(::clock))
    }

    @Test
    fun `a landed jump`() {
        val jumped = Jumped(JumpResult(Status.PERSONAL, 103500), Landing.LANDED)
        assertEquals("Jumped to 1:43: the part you replay most.", describeJump(jumped))
    }

    @Test
    fun `a slipped jump says what happened`() {
        val jumped = Jumped(JumpResult(Status.PINNED, 72000), Landing.SLIPPED)
        assertEquals("Tried 1:12 (your pinned spot), but Spotify did not move there.", describeJump(jumped))
    }

    @Test
    fun `no jump says why`() {
        val noData = describeJump(Jumped(JumpResult(Status.NO_DATA), null))
        assertTrue(noData.startsWith("No jump: ") && "nothing pinned or learned" in noData)
        val closed = describeJump(Jumped(JumpResult(Status.PLAYER_UNAVAILABLE), null))
        assertTrue(closed.startsWith("No jump: ") && "Spotify is not playing" in closed)
    }

    @Test
    fun `the notification says what a jump would do`() {
        assertEquals("Jumps to 1:46: your pinned spot.", describeTarget(JumpResult(Status.PINNED, 106200)))
        assertEquals("No jump: nothing pinned or learned for this song yet.", describeTarget(JumpResult(Status.NO_DATA)))
        assertEquals("Spotify is not playing anything.", describeTarget(JumpResult(Status.PLAYER_UNAVAILABLE)))
    }

    @Test
    fun pins() {
        assertEquals("Pinned 1:12. The jump lands 1.5 s before it.", describePin(72000))
        assertEquals("Pinned 1:23. The jump lands right there.", describePin(83000, exact = true))
        assertTrue(describePin(null).startsWith("Nothing pinned"))
    }

    @Test
    fun `removing a pin says what happened`() {
        assertEquals("Pin removed.", describeUnpin(true))
        assertEquals("Nothing to remove: this song has no pin.", describeUnpin(false))
    }

    @Test
    fun `the logbook says what it did`() {
        assertEquals("Forgot this song: its pin and what was learned.", describeForget(true))
        assertEquals("Not tracking this song. Its pin stays.", describeIgnore(true))
        assertEquals("Learning paused. Pins still work.", describePause(true))
        assertEquals(NO_SONG, describeTrackAgain(false))
    }

    @Test
    fun `a typed time must be a time within the song`() {
        assertEquals("Type a time like 1:23.", typedTimeProblem(parseClock("abc"), 192000))
        assertEquals("This song is 3:12 long.", typedTimeProblem(parseClock("3:30"), 192000))
        assertNull(typedTimeProblem(parseClock("1:23"), 192000))
        assertNull(typedTimeProblem(parseClock("3:12"), 192000))
    }

    @Test
    fun `parse clock`() {
        val cases = mapOf("1:23" to 83000L, " 1:23 " to 83000L, "1:23.5" to 83500L, "0:07" to 7000L, "83" to 83000L, "12:00" to 720000L)
        for ((text, ms) in cases) assertEquals(ms, parseClock(text), text)
    }

    @Test
    fun `parse clock refuses`() {
        for (text in listOf("", "abc", "1:60", "1:-5", "-1:20", "1:2:3", "nan")) assertNull(parseClock(text), text)
    }
}
