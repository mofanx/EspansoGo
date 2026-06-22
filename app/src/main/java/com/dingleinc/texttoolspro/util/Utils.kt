package com.dingleinc.texttoolspro.util

import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.TextStyle
import java.time.temporal.IsoFields
import java.util.Locale

object Utils {
    private val chronoToDotNet = linkedMapOf(
        "%Y" to "yyyy",
        "%y" to "yy",
        "%m" to "MM",
        "%B" to "MMMM",
        "%b" to "MMM",
        "%h" to "MMM",
        "%d" to "dd",
        "%e" to "d",
        "%A" to "dddd",
        "%a" to "ddd",
        "%H" to "HH",
        "%I" to "hh",
        "%p" to "tt",
        "%M" to "mm",
        "%S" to "ss",
        "%n" to "\n",
        "%t" to "\t",
        "%%" to "%",
        "%N" to "SSSSSSS",
        "%z" to "XXX"
    )

    private val dotNetToChrono = linkedMapOf(
        "MMMM" to "%B",
        "MMM" to "%b",
        "MM" to "%m",
        "yyyy" to "%Y",
        "yy" to "%y",
        "dddd" to "%A",
        "ddd" to "%a",
        "dd" to "%d",
        "HH" to "%H",
        "hh" to "%I",
        "tt" to "%p",
        "mm" to "%M",
        "ss" to "%S",
        "XXX" to "%z",
        "SSSSSSS" to "%N"
    )

    fun getTheRealFormat(format: String): String {
        val now = LocalDateTime.now()
        var result = format

        // Handle compound formats first (longer patterns)
        result = result.replace("%D", "MM/dd/yyyy")
        result = result.replace("%F", "yyyy/MM/dd")
        result = result.replace("%R", "HH:mm")
        result = result.replace("%T", "HH:mm:ss")
        result = result.replace("%r", "hh:mm:ss tt")

        // Handle direct-eval formats (no direct .NET equivalent, evaluate at conversion time)
        result = result.replace("%j", now.dayOfYear.toString())
        result = result.replace("%w", now.dayOfWeek.getDisplayName(TextStyle.FULL, Locale.ENGLISH))
        result = result.replace("%u", (now.dayOfWeek.value).toString())
        result = result.replace("%C", (now.year / 100).toString())
        result = result.replace("%G", now.with(IsoFields.WEEK_BASED_YEAR).toString())
        result = result.replace("%V", String.format("%02d", now.get(IsoFields.WEEK_OF_WEEK_BASED_YEAR)))
        result = result.replace("%Z", ZoneId.systemDefault().id)
        result = result.replace("%s", (now.atZone(ZoneId.systemDefault()).toEpochSecond()).toString())

        // Token-based replacement: use placeholders to avoid cascading replacements
        val tokens = mutableListOf<Pair<String, String>>()
        var placeholderIndex = 0
        for ((chrono, dotnet) in chronoToDotNet) {
            if (result.contains(chrono)) {
                val placeholder = "\u0000${placeholderIndex}\u0000"
                result = result.replace(chrono, placeholder)
                tokens.add(placeholder to dotnet)
                placeholderIndex++
            }
        }
        for ((placeholder, dotnet) in tokens) {
            result = result.replace(placeholder, dotnet)
        }

        return result
    }

    fun getOriginalFormat(format: String): String {
        var result = format

        // Handle compound formats first (longer patterns)
        result = result.replace("MM/dd/yyyy", "%D")
        result = result.replace("yyyy/MM/dd", "%F")
        result = result.replace("HH:mm:ss", "%T")
        result = result.replace("hh:mm:ss tt", "%r")
        result = result.replace("HH:mm", "%R")

        // Handle direct-eval reverse formats (best-effort, these are lossy)
        // %j, %w, %u, %C, %G, %V, %Z, %s are evaluated at import time and cannot be reversed

        // Token-based replacement: from longest to shortest
        val tokens = mutableListOf<Pair<String, String>>()
        var placeholderIndex = 0
        for ((dotnet, chrono) in dotNetToChrono) {
            if (result.contains(dotnet)) {
                val placeholder = "\u0000${placeholderIndex}\u0000"
                result = result.replace(dotnet, placeholder)
                tokens.add(placeholder to chrono)
                placeholderIndex++
            }
        }
        for ((placeholder, chrono) in tokens) {
            result = result.replace(placeholder, chrono)
        }

        return result
    }
}
