package app.hookjump.core

import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import kotlinx.serialization.json.put
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.TestFactory

/** Choosing the jump, checked against spec/vectors/target_choose.json. */
class TargetTest {
    @TestFactory
    fun choose() = vectorTests("target_choose.json") { case ->
        val pin = case.field("pin")?.jsonObject?.let {
            Pin(it.getValue("at_ms").jsonPrimitive.long, it.getValue("typed").jsonPrimitive.boolean)
        }
        val favorite = case.field("favorite")?.jsonObject?.let {
            Favorite(
                it.getValue("target_ms").jsonPrimitive.long,
                it.getValue("points").jsonPrimitive.int,
                it.getValue("listens").jsonPrimitive.int,
            )
        }
        val result = choose(case.getValue("track_loaded").jsonPrimitive.boolean, pin, favorite)
        val found = buildJsonObject {
            put("status", result.status.value)
            put("target_ms", result.targetMs)
        }
        assertEquals(case.getValue("expected"), found)
    }
}
