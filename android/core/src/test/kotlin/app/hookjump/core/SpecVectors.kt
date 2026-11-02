package app.hookjump.core

import java.io.File
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.jupiter.api.DynamicTest

// Load the language-neutral test vectors in spec/vectors/, shared with Python.

private val vectorsDir = File(System.getProperty("spec.vectors") ?: error("spec.vectors is not set"))

/** One dynamic test per case of a vector file, named after the case. */
fun vectorTests(file: String, check: (JsonObject) -> Unit): List<DynamicTest> {
    val root = Json.parseToJsonElement(File(vectorsDir, file).readText()).jsonObject
    val cases = root.getValue("cases").jsonArray.map { it.jsonObject }
    val names = cases.map { it.getValue("name").jsonPrimitive.content }
    check(names.size == names.toSet().size) { "duplicate case names in $file" }
    return cases.zip(names).map { (case, name) -> DynamicTest.dynamicTest(name) { check(case) } }
}

/** The field, or null when it is missing or JSON null. */
fun JsonObject.field(key: String): JsonElement? = this[key]?.takeUnless { it is JsonNull }
