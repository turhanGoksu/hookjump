package app.hookjump.player

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class NeedleTest {
    private val player = FakeTurntable()
    private val ear = Ear()
    private val needle = Needle(player, ear)
    private var landing: Landing? = null

    private fun drop(targetMs: Long = 100000) = needle.drop("a", targetMs, player.atMs) { landing = it }

    @Test
    fun `a jump lands when the player reports the new place`() {
        drop()
        assertNull(landing) // waits for the player
        needle.heard(player.report(), player.atMs)
        assertEquals(Landing.LANDED, landing)
        assertEquals(1, player.places)
    }

    @Test
    fun `a report from before the move is not an answer`() {
        player.atMs += 5000
        drop()
        needle.heard(player.read()!!.copy(atMs = player.atMs - 1000, positionMs = 30000), player.atMs)
        assertNull(landing)
        assertEquals(1, player.places) // no second try because of an old report
    }

    @Test
    fun `a silent player is tried once more, then the jump has slipped`() {
        drop()
        needle.tick(player.atMs + Needle.SETTLE_MS)
        assertEquals(2, player.places)
        assertNull(landing)
        needle.tick(player.atMs + 2 * Needle.SETTLE_MS)
        assertEquals(Landing.SLIPPED, landing)
        assertEquals(2, player.places)
    }

    @Test
    fun `a player that ignores the first move lands on the second`() {
        player.ignores = 1
        drop()
        needle.heard(player.report(), player.atMs) // still at 0:30
        assertEquals(2, player.places)
        needle.heard(player.report(), player.atMs)
        assertEquals(Landing.LANDED, landing)
    }

    @Test
    fun `a player that ignores both moves is reported`() {
        player.ignores = 2
        drop()
        needle.heard(player.report(), player.atMs)
        needle.heard(player.report(), player.atMs)
        assertEquals(Landing.SLIPPED, landing)
    }

    @Test
    fun `landing close enough counts`() {
        drop(100000)
        needle.heard(player.report()!!.copy(positionMs = 101500), player.atMs)
        assertEquals(Landing.LANDED, landing)
    }

    @Test
    fun `a new track is never moved`() {
        player.switchTo = "b"
        drop()
        assertEquals(Landing.TRACK_CHANGED, landing)
        assertEquals("b", player.track)
        assertEquals(0L, player.positionMs)
    }

    @Test
    fun `a track change while waiting is reported`() {
        drop()
        needle.heard(player.report()!!.copy(trackId = "b"), player.atMs)
        assertEquals(Landing.TRACK_CHANGED, landing)
    }

    @Test
    fun `a closed player is reported`() {
        player.running = false
        drop()
        assertEquals(Landing.NO_PLAYER, landing)
    }

    @Test
    fun `the jump marks its own move for the ear`() {
        ear.hear(player.read(), player.atMs)
        drop()
        val samples = ear.hear(player.report(), player.atMs)
        assertEquals(true, samples.last().ownSeek)
    }
}
