package com.dingleinc.texttoolspro.data

import com.fasterxml.jackson.core.JsonParser
import com.fasterxml.jackson.core.JsonToken
import com.fasterxml.jackson.databind.DeserializationContext
import com.fasterxml.jackson.databind.JsonDeserializer
import com.fasterxml.jackson.databind.annotation.JsonDeserialize
import kotlinx.serialization.Serializable

@Serializable
data class Params(
    var echo: String? = null,
    var format: String? = null,
    var offset: Long = 0,
    var cmd: String? = null,
    var layout: String? = null,
    var choices: MutableList<String>? = null,
    @JsonDeserialize(using = ValuesDeserializer::class)
    var values: MutableList<String>? = null
) {
    constructor(og: Params) : this(
        og.echo, og.format, og.offset, og.cmd, og.layout,
        og.choices?.toMutableList(),
        og.values?.toMutableList()
    )
}

@Serializable
data class Var(
    var name: String? = null,
    var type: String? = null,
    var params: Params = Params()
) {
    constructor(og: Var) : this(og.name, og.type, Params(og.params))
}

@Serializable
data class FormOption(
    var multiline: Boolean = false,
    var type: String? = null,
    var values: MutableList<String>? = null
)

@Serializable
data class Match(
    var trigger: String? = null,
    var replace: String? = null,
    var vars: MutableList<Var>? = null,
    var form: String? = null,
    var formFields: HashMap<String, FormOption>? = null,
    var word: Boolean = false,
    var triggers: MutableList<String>? = null,
    var leftWord: Boolean = false,
    var rightWord: Boolean = false,
    var propagateCase: Boolean = false,
    var uppercaseStyle: String? = null,
    var regex: String? = null
) {
    constructor(og: Match) : this(
        og.trigger, og.replace,
        og.vars?.map { Var(it) }?.toMutableList(),
        og.form,
        og.formFields?.let { HashMap(it) },
        og.word,
        og.triggers?.toMutableList(),
        og.leftWord,
        og.rightWord,
        og.propagateCase,
        og.uppercaseStyle,
        og.regex
    )
}

@Serializable
data class DictWrapper(
    @kotlinx.serialization.SerialName("global_vars")
    var globalVars: MutableList<Var>? = null,
    var matches: MutableList<Match>? = null
)

class ValuesDeserializer : JsonDeserializer<MutableList<String>>() {
    override fun deserialize(p: JsonParser, ctxt: DeserializationContext): MutableList<String>? {
        return when (p.currentToken) {
            JsonToken.VALUE_NULL -> null
            JsonToken.VALUE_STRING -> {
                val s = p.valueAsString
                if (s.isNullOrBlank()) mutableListOf()
                else s.split("\n").map { it.trim() }.filter { it.isNotEmpty() }.toMutableList()
            }
            JsonToken.START_ARRAY -> {
                val text = p.readValueAsTree<com.fasterxml.jackson.databind.node.ArrayNode>()
                val list = mutableListOf<String>()
                text.forEach { node -> list.add(node.asText()) }
                list
            }
            else -> mutableListOf()
        }
    }
}
