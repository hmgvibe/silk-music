package app.silk.data

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.io.File
import java.io.RandomAccessFile
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.PI
import kotlin.math.exp
import kotlin.math.min
import kotlin.math.sin
import kotlin.math.tanh
import kotlin.random.Random

/** Offline content for layout and audio checks: original synth tracks, drawn covers and placeholder lyrics. */
class DemoSource(context: Context) : MusicSource {
    private class Album(
        val id: String, val title: String, val artist: String,
        val back: Int, val mid: Int, val light: Int,
        val root: DoubleArray, val lyrics: List<String>,
    )

    private val albums = listOf(
        Album("tide", "潮間帶", "林間信號", Color.rgb(9, 34, 66), Color.rgb(16, 92, 118), Color.rgb(126, 214, 214),
            doubleArrayOf(146.83, 116.54, 174.61, 130.81),
            listOf("退潮的時候", "腳印先學會了離開", "風把鹽留在袖口", "我把名字留給海", "燈塔還亮著", "像沒說完的一句話", "等下一次漲潮", "再替我說出來")),
        Album("noon", "赤色午後", "夏日電台", Color.rgb(88, 16, 24), Color.rgb(172, 44, 38), Color.rgb(255, 168, 108),
            doubleArrayOf(110.0, 130.81, 98.0, 146.83),
            listOf("柏油路在發燙", "收音機唱到一半", "你笑著闖紅燈", "整條街都變慢", "汽水冒著氣泡，像整個夏天都在輕輕地笑", "夏天沒有答案", "我們往西邊走", "追一個黃昏")),
        Album("mist", "晨霧", "小島日記", Color.rgb(226, 213, 194), Color.rgb(192, 205, 204), Color.rgb(255, 251, 240),
            doubleArrayOf(196.0, 164.81, 220.0, 146.83),
            listOf("窗外還沒有醒", "霧替山收好輪廓", "水壺輕輕地響", "日子重新開始", "你留下的杯子", "還有一點溫熱", "今天不必趕路", "慢慢走就好")),
        Album("night", "夜行列車", "月台樂隊", Color.rgb(16, 42, 38), Color.rgb(28, 110, 88), Color.rgb(210, 226, 140),
            doubleArrayOf(123.47, 98.0, 146.83, 110.0),
            listOf("末班車進站了", "窗上有城市的影子", "我數著隧道的燈", "數到你的名字", "下一站還很遠", "耳機裡下著雨", "如果天亮以前", "能夠到達這裡")),
        Album("violet", "紫色回音", "第七個房間", Color.rgb(52, 34, 92), Color.rgb(122, 76, 170), Color.rgb(240, 180, 214),
            doubleArrayOf(138.59, 103.83, 155.56, 123.47),
            listOf("走廊盡頭有光", "門縫裡傳來鋼琴", "我喊了一聲", "回音比我更輕", "牆上的時鐘", "忘記往前走", "這一晚這麼長", "夠我慢慢地懂")),
    )

    private val directory = File(context.cacheDir, "demo-v2").apply { mkdirs() }
    private val likedIds = mutableSetOf<String>()
    private val playlists = mutableListOf<Pair<Collection, MutableList<Song>>>()

    override val isSignedIn: Boolean = true
    override var streamQuality: String = "best"

    override suspend fun profile() = UserProfile("晚風", "")

    override suspend fun guessYouLike(): List<Song> {
        delay(200)
        return albums.shuffled().flatMap { tracksOf(it).take(2) }.take(6)
    }

    override suspend fun dailySongs(): List<Song> {
        delay(200)
        return albums.flatMap { tracksOf(it) }
    }

    override suspend fun recommendedSongs(): List<Song> {
        delay(180)
        return albums.flatMap { tracksOf(it).drop(1).take(2) }.take(8)
    }

    override suspend fun recommendedPlaylists(): List<Collection> {
        delay(180)
        return albums.map { Collection(CollectionKind.Playlist, it.id, "${it.title}的午後歌單", "4 首", coverOf(it)) }
    }

    override suspend fun newAlbums(): List<Collection> {
        delay(180)
        return albums.reversed().map { Collection(CollectionKind.Album, it.id, it.title, it.artist, coverOf(it)) }
    }

    override suspend fun library(): List<Collection> {
        delay(240)
        return playlists.map { (playlist, songs) -> playlist.copy(subtitle = "${songs.size} 首", cover = songs.firstOrNull()?.cover.orEmpty()) } +
            albums.map { Collection(CollectionKind.Album, it.id, it.title, it.artist, coverOf(it)) }
    }

    override suspend fun editablePlaylists(): List<Collection> {
        delay(120)
        return playlists.map { it.first }
    }

    override suspend fun createPlaylist(name: String): Collection {
        delay(200)
        val playlist = Collection(CollectionKind.Owned, "demo-list-${playlists.size + 1}", name, "歌單", "")
        playlists.add(0, playlist to mutableListOf())
        return playlist
    }

    override suspend fun addToPlaylist(playlist: Collection, song: Song) {
        delay(160)
        val songs = playlists.first { it.first.id == playlist.id }.second
        if (songs.none { it.mid == song.mid }) songs += song
    }

    override suspend fun liked(): List<Song> {
        delay(240)
        val all = albums.flatMap { tracksOf(it) }
        return all.filter { it.mid in likedIds }.ifEmpty { all.take(6) }
    }

    override suspend fun setLiked(song: Song, liked: Boolean) {
        delay(120)
        if (liked) likedIds += song.mid else likedIds -= song.mid
    }

    override suspend fun songs(collection: Collection): List<Song> = when (collection.kind) {
        CollectionKind.Daily -> dailySongs()
        CollectionKind.Guess -> guessYouLike()
        CollectionKind.Liked -> liked()
        CollectionKind.Owned -> playlists.first { it.first.id == collection.id }.second.toList()
        else -> tracksOf(albums.first { it.id == collection.id })
    }

    override suspend fun search(keyword: String): List<Song> = albums.flatMap { tracksOf(it) }
        .filter { it.title.contains(keyword, true) || it.artist.contains(keyword, true) }

    override suspend fun searchCollections(keyword: String, kind: CollectionKind): List<Collection> {
        delay(200)
        val matches = albums.filter { it.title.contains(keyword, true) || it.artist.contains(keyword, true) }
        return if (kind == CollectionKind.Album) {
            matches.map { Collection(CollectionKind.Album, it.id, it.title, "${it.artist} · 2026", coverOf(it)) }
        } else {
            matches.map { Collection(CollectionKind.Playlist, it.id, "${it.title}的午後歌單", "4 首 · 晚風", coverOf(it)) }
        }
    }

    override fun singerPage(artist: Artist): Collection {
        val album = albums.first { it.id == artist.mid }
        return Collection(CollectionKind.Singer, album.id, album.artist, "4 首", coverOf(album))
    }

    override suspend fun searchSingers(keyword: String): List<Collection> {
        delay(180)
        return albums.filter { it.artist.contains(keyword, true) }
            .map { Collection(CollectionKind.Singer, it.id, it.artist, "4 首", coverOf(it)) }
    }

    override suspend fun stream(mid: String): String? = withContext(Dispatchers.Default) {
        val album = albums.first { mid.startsWith(it.id) }
        val variant = mid.substringAfterLast('-').toInt()
        val file = File(directory, "$mid.wav")
        if (!file.exists()) synthesize(file, album, variant)
        file.toURI().toString()
    }

    override suspend fun lyrics(mid: String): List<LyricEntry> {
        val album = albums.first { mid.startsWith(it.id) }
        // A song-like shape: credits, a short intro, eight lines, a marked break, then the last four lines again.
        val lines = mutableListOf(LyricEntry(0.0, "作詞：${album.artist}"), LyricEntry(1.2, "作曲：${album.artist}"))
        album.lyrics.forEachIndexed { i, text -> lines += LyricEntry(4.6 + i * 2.9, text) }
        lines += LyricEntry(27.4, "")
        album.lyrics.drop(4).forEachIndexed { i, text -> lines += LyricEntry(33.0 + i * 2.5, text) }
        return lines
    }

    override suspend fun createQr(channel: String): QrTicket = throw UnsupportedOperationException()
    override suspend fun checkQr(key: String) = QrStatus(801, "")
    override suspend fun signOut() = Unit

    private fun tracksOf(album: Album): List<Song> {
        val suffix = listOf("", "（夜版）", "（現場）", "（鋼琴）")
        return (0 until 4).map { i ->
            Song("${album.id}-$i", album.title + suffix[i], album.artist, album.title, album.id, 42, coverOf(album),
                singers = listOf(Artist(album.id, album.artist)))
        }
    }

    private fun coverOf(album: Album): String {
        val file = File(directory, "${album.id}.png")
        if (file.exists()) return file.toURI().toString()
        val size = 720f
        val bitmap = Bitmap.createBitmap(size.toInt(), size.toInt(), Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        val seed = albums.indexOf(album)
        canvas.drawColor(album.back)
        val cx = size * (.3f + seed * .08f)
        val cy = size * (.62f - seed * .05f)
        paint.color = album.mid
        canvas.drawCircle(cx, cy, size * .46f, paint)
        paint.color = album.light
        canvas.drawCircle(cx + size * .34f, cy - size * .34f, size * .17f, paint)
        val y = size * (.78f - seed * .03f)
        val wave = Path().apply {
            moveTo(0f, y)
            cubicTo(size * .25f, y - 130, size * .42f, y + 90, size * .6f, y)
            cubicTo(size * .75f, y - 70, size * .9f, y - 60, size, y - 20)
            lineTo(size, size)
            lineTo(0f, size)
            close()
        }
        paint.color = Color.argb(190, Color.red(album.back), Color.green(album.back), Color.blue(album.back))
        canvas.drawPath(wave, paint)
        file.outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        bitmap.recycle()
        return file.toURI().toString()
    }

    private fun synthesize(file: File, album: Album, variant: Int) {
        val rate = 44100
        val seconds = 42.0
        val beat = 60 / 98.0
        val length = (seconds * rate).toInt()
        val random = Random(album.id.length * 31 + variant)
        val ratios = doubleArrayOf(1.0, 1.2599, 1.4983, 2.0)
        val arrangement = listOf(
            0.0 to .12, 5.0 + variant to .34, 11.0 + variant to .58, 17.0 + variant to 1.0, 24.0 + variant to .3,
            29.0 + variant to 1.0, 36.0 to .42, 42.0 to .1,
        )
        fun gainAt(time: Double): Double {
            for (s in 0 until arrangement.size - 1) {
                val (from, a) = arrangement[s]
                val (to, b) = arrangement[s + 1]
                if (time > to) continue
                val x = ((time - from) / min(1.6, to - from)).coerceIn(0.0, 1.0)
                return a + (b - a) * x * x * (3 - 2 * x)
            }
            return arrangement.last().second
        }

        val data = ByteBuffer.allocate(length * 4).order(ByteOrder.LITTLE_ENDIAN)
        val tau = 2 * PI
        for (i in 0 until length) {
            val t = i / rate.toDouble()
            val beats = t / beat
            val bar = (beats / 4).toInt() % album.root.size
            val root = album.root[bar] * if (variant == 2) 1.5 else 1.0
            val pan = sin(t * .7) * .4
            var pad = 0.0
            for (ratio in ratios) for (h in 1..4) pad += sin(tau * root * ratio * h * t * (1 + .0015 * h)) / (h * h)
            pad *= .035 * (.75 + .25 * sin(t * 1.3))
            val bass = sin(tau * root / 2 * t) * .22

            val step = beats * 2
            val arpIndex = step.toInt()
            val arpPhase = step - arpIndex
            val arpPitch = root * 2 * ratios[(arpIndex * (variant + 1)) % ratios.size]
            val arp = (sin(tau * arpPitch * t) + .4 * sin(tau * arpPitch * 2.01 * t)) * exp(-arpPhase * 3.4) * .13
            val arpPan = if (arpIndex % 2 == 0) -.65 else .65

            val beatPhase = (beats % 2) * beat
            val kick = sin(tau * (48 * beatPhase + 34 * (1 - exp(-beatPhase * 24)))) * exp(-beatPhase * 9) * .42
            val hatPhase = (beats + .5) % 1 * beat
            val hat = (random.nextDouble() * 2 - 1) * exp(-hatPhase * 42) * .09

            val l = pad * (1 - pan) + bass + kick + arp * (1 - arpPan) * .75 + hat * 1.2
            val r = pad * (1 + pan) + bass + kick + arp * (1 + arpPan) * .75 + hat * .6
            val fade = min(1.0, min(t / .4, (seconds - t) / .8))
            val gain = gainAt(t)
            data.putShort((tanh(l) * fade * .8 * gain * 32767).toInt().toShort())
            data.putShort((tanh(r) * fade * .8 * gain * 32767).toInt().toShort())
        }

        val temporary = File(file.path + ".tmp")
        RandomAccessFile(temporary, "rw").use { out ->
            out.setLength(0)
            val header = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN)
            header.put("RIFF".toByteArray()).putInt(36 + length * 4).put("WAVE".toByteArray())
            header.put("fmt ".toByteArray()).putInt(16).putShort(1.toShort()).putShort(2.toShort()).putInt(rate).putInt(rate * 4)
                .putShort(4.toShort()).putShort(16.toShort())
            header.put("data".toByteArray()).putInt(length * 4)
            out.write(header.array())
            out.write(data.array())
        }
        temporary.renameTo(file)
    }
}
