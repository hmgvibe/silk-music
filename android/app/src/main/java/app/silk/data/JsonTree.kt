package app.silk.data

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/** Lenient readers for API answers whose field names vary from route to route. */
internal object JsonTree {
    fun str(node: JsonElement?, vararg keys: String): String? {
        if (node !is JsonObject) return null
        for (key in keys) {
            val value = node[key] as? JsonPrimitive ?: continue
            if (value is JsonNull) continue
            val text = value.content
            if (text.isNotBlank()) return text
        }
        return null
    }

    fun int(node: JsonElement?, vararg keys: String): Int? = str(node, *keys)?.toIntOrNull()

    fun long(node: JsonElement?, vararg keys: String): Long? = str(node, *keys)?.toLongOrNull()

    fun obj(node: JsonElement?, key: String): JsonObject? = (node as? JsonObject)?.get(key) as? JsonObject

    fun array(node: JsonElement?, key: String): JsonArray? = (node as? JsonObject)?.get(key) as? JsonArray

    fun isTrue(node: JsonElement?, key: String): Boolean =
        ((node as? JsonObject)?.get(key) as? JsonPrimitive)?.content == "true"

    /** The first value under any of these keys, searching depth first. */
    fun deep(node: JsonElement?, vararg keys: String): String? = when (node) {
        is JsonObject -> str(node, *keys) ?: node.values.firstNotNullOfOrNull { deep(it, *keys) }
        is JsonArray -> node.firstNotNullOfOrNull { deep(it, *keys) }
        else -> null
    }

    fun deepObject(node: JsonElement?, key: String): JsonObject? {
        if (node !is JsonObject) return null
        (node[key] as? JsonObject)?.let { return it }
        return node.values.firstNotNullOfOrNull { deepObject(it, key) }
    }

    /** The first array whose items are objects, searching depth first. */
    fun firstObjectArray(node: JsonElement?): JsonArray? {
        if (node !is JsonObject) return null
        for (value in node.values) {
            if (value is JsonArray && value.isNotEmpty() && value[0] is JsonObject) return value
            if (value is JsonObject) firstObjectArray(value)?.let { return it }
        }
        return null
    }
}

/** Titles arrive with HTML entities. */
internal fun cleanText(value: String): String {
    if (!value.contains('&')) return value.trim()
    val named = mapOf("amp" to "&", "lt" to "<", "gt" to ">", "quot" to "\"", "apos" to "'", "nbsp" to " ")
    return Regex("&(#x[0-9a-fA-F]+|#[0-9]+|[a-zA-Z]+);").replace(value) { match ->
        val body = match.groupValues[1]
        when {
            body.startsWith("#x") -> body.drop(2).toIntOrNull(16)?.let { String(Character.toChars(it)) }
            body.startsWith("#") -> body.drop(1).toIntOrNull()?.let { String(Character.toChars(it)) }
            else -> named[body]
        } ?: match.value
    }.trim()
}
