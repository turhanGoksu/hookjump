package app.hookjump.core

import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import kotlinx.serialization.json.put
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestFactory
import org.junit.jupiter.api.assertThrows

/** The listener's favorite part, checked against spec/vectors/heatmap_favorite.json. */
class HeatmapTest {
    @TestFactory
    fun favorite() = vectorTests("heatmap_favorite.json") { case ->
        val seeks = case.getValue("seeks").jsonArray.map { it.jsonObject }.map {
            SeekEvent(
                trackId = "a",
                listen = it.getValue("listen").jsonPrimitive.int,
                fromMs = it.getValue("from_ms").jsonPrimitive.long,
                toMs = it.getValue("to_ms").jsonPrimitive.long,
            )
        }
        val found = favorite(seeks)?.let {
            buildJsonObject {
                put("target_ms", it.targetMs)
                put("points", it.points)
                put("listens", it.listens)
            }
        } ?: JsonNull
        assertEquals(case.getValue("expected"), found)
    }

    @Test
    fun `seeks of several tracks are refused`() {
        val seeks = listOf(SeekEvent("a", 1, 9000, 1000), SeekEvent("b", 1, 9000, 1000))
        assertThrows<IllegalArgumentException> { favorite(seeks) }
    }
}
