package app.hookjump.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

/** A jump result has a target exactly when its status says it should. */
class ModelsTest {
    @Test
    fun `status values match the spec`() {
        // spec/ALGORITHM.md section 2 and every JSON vector use these strings.
        assertEquals(
            setOf("pinned", "personal", "no_data", "player_unavailable"),
            Status.entries.map { it.value }.toSet(),
        )
        assertEquals(setOf(Status.PINNED, Status.PERSONAL), Status.entries.filter { it.hasTarget }.toSet())
    }

    @Test
    fun `a target status needs a target`() {
        for (status in Status.entries.filter { it.hasTarget }) {
            assertEquals(60500L, JumpResult(status, 60500).targetMs)
            assertThrows<IllegalArgumentException> { JumpResult(status) }
        }
    }

    @Test
    fun `a status without a target refuses one`() {
        for (status in Status.entries.filterNot { it.hasTarget }) {
            assertEquals(null, JumpResult(status).targetMs)
            assertThrows<IllegalArgumentException> { JumpResult(status, 60500) }
        }
    }

    @Test
    fun `a target cannot be negative`() {
        assertThrows<IllegalArgumentException> { JumpResult(Status.PERSONAL, -1) }
        assertEquals(0L, JumpResult(Status.PERSONAL, 0).targetMs)
    }
}
