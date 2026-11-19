package app.silk.data

/** One row in the lyric view. Mirrors the Windows app's rules for credits, breaks and untimed lyrics. */
data class LyricLine(
    val time: Double,
    val text: String,
    /** False for a placeholder message or lyrics without timestamps; they are shown but cannot be tapped to seek. */
    val timed: Boolean,
    /** A title or "lyrics by" line at the start, shown small and never highlighted. */
    val isCredit: Boolean,
    /** An instrumental gap: shown as three dots that fill until [end]. */
    val isBreak: Boolean,
    val end: Double,
)

object Lrc {
    private val time = Regex("""\[(\d{1,3}):(\d{1,2}(?:\.\d{1,3})?)]""")
    private val tag = Regex("""^\[[A-Za-z]+:.*]$""")
    private val creditRoles = listOf(
        "詞", "词", "曲", "編曲", "编曲", "製作", "制作", "監製", "监制", "混音", "和聲", "和声", "錄音", "录音", "母帶", "母带",
        "吉他", "貝斯", "贝斯", "鼓", "鍵盤", "键盘", "弦樂", "弦乐", "出品", "發行", "发行", "企劃", "企划", "統籌", "统筹",
        "OP", "SP", "Lyric", "Compose", "Arrange", "Produce", "Mix", "Master",
    )

    /** Shortest gap, in seconds, that is shown as a break. */
    private const val BREAK_GAP = 4.0

    fun parse(raw: String): List<LyricEntry> {
        val text = cleanText(raw.replace("\r", ""))
        val lines = mutableListOf<LyricEntry>()
        for (line in text.split('\n')) {
            val matches = time.findAll(line).toList()
            if (matches.isEmpty()) continue
            var content = time.replace(line, "").trim()
            // A timed empty line marks where the singing stops; it is kept so breaks can be shown.
            if (content == "//") content = ""
            for (match in matches) {
                val minutes = match.groupValues[1].toInt()
                val seconds = match.groupValues[2].toDouble()
                lines += LyricEntry(minutes * 60 + seconds, content)
            }
        }
        lines.sortBy { it.time }
        if (lines.isNotEmpty()) return lines
        // Lyrics without any timestamps are still worth showing, as plain text.
        return text.split('\n').map { it.trim() }.filter { it.isNotEmpty() && !tag.matches(it) }.map { LyricEntry(0.0, it) }
    }

    fun lines(entries: List<LyricEntry>): List<LyricLine> {
        val timed = entries.size < 2 || entries.map { it.time }.distinct().size > 1
        var credits = 0
        while (credits < entries.size && credits < 12 && isCredit(entries[credits].text, credits)) credits++
        // Only a run followed by real lyrics counts; a song of credit-like lines is left alone.
        if (credits == entries.size) credits = 0

        val result = mutableListOf<LyricLine>()
        entries.forEachIndexed { i, entry ->
            val next = entries.getOrNull(i + 1)?.time ?: (entry.time + 6)
            if (entry.text.isEmpty()) {
                // Empty lines are breaks only when the gap is long enough to notice, and never before the first lyric.
                if (timed && i >= credits && result.any { !it.isCredit && !it.isBreak } && next - entry.time >= BREAK_GAP) {
                    result += LyricLine(entry.time, "", timed = true, isCredit = false, isBreak = true, end = next)
                }
                return@forEachIndexed
            }
            result += LyricLine(entry.time, entry.text, timed, isCredit = i < credits, isBreak = false, end = next)
        }
        return result
    }

    private fun isCredit(text: String, index: Int): Boolean {
        if (text.isEmpty()) return index > 0
        if (index == 0 && text.contains(" - ")) return true
        val colon = text.indexOfFirst { it == '：' || it == ':' }
        if (colon <= 0 || colon > 14) return false
        val role = text.substring(0, colon)
        return creditRoles.any { role.contains(it, ignoreCase = true) }
    }
}
