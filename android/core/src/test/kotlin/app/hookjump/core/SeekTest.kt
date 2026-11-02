package app.hookjump.core

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import kotlinx.serialization.json.put
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.TestFactory

/** Seek detection, checked against spec/vectors/seek_detect.json. */
class SeekTest {
    @TestFactory
    fun detect() = vectorTests("seek_detect.json") { case ->
        val samples = case.getValue("samples").jsonArray.map { it.jsonObject }.map {
            PlaybackSample(
                atMs = it.getValue("at_ms").jsonPrimitive.long,
                trackId = it.field("track_id")?.jsonPrimitive?.content,
                playing = it.getValue("playing").jsonPrimitive.boolean,
                positionMs = it.field("position_ms")?.jsonPrimitive?.long,
                ownSeek = it.field("own_seek")?.jsonPrimitive?.boolean ?: false,
            )
        }
        val found = detect(samples).map { event ->
            buildJsonObject {
                put("type", if (event is PlaySpan) "play" else "seek")
                put("track_id", event.trackId)
                put("listen", event.listen)
                put("from_ms", event.fromMs)
                put("to_ms", event.toMs)
            }
        }
        assertEquals(case.getValue("expected"), JsonArray(found))
    }
}
