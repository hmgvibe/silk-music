package app.silk.data

import android.net.Uri
import android.util.Base64
import app.silk.bridge.Bridge
import app.silk.data.JsonTree.array
import app.silk.data.JsonTree.deep
import app.silk.data.JsonTree.deepObject
import app.silk.data.JsonTree.firstObjectArray
import app.silk.data.JsonTree.int
import app.silk.data.JsonTree.isTrue
import app.silk.data.JsonTree.long
import app.silk.data.JsonTree.obj
import app.silk.data.JsonTree.str
import kotlinx.coroutines.CancellationException
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonObject
import java.io.IOException

/** Reads QQ Music through the bundled bridge. A port of the Windows app's QqMusicSource. */
class QqMusicSource(private val bridge: Bridge) : MusicSource {
    override val isSignedIn: Boolean get() = !bridge.token.isNullOrEmpty()

    override var streamQuality: String = "best"

    override suspend fun profile(): UserProfile? {
        if (!isSignedIn) return null
        val root = bridge.get("/user/detail")
        if (int(root, "code") == 401) {
            bridge.clearToken()
            return null
        }
        val name = deep(root, "nickname", "nick", "Nick") ?: "QQ 音樂使用者"
        val avatar = deep(root, "avatarUrl", "logo", "headurl", "headpic", "Avatar") ?: ""
        return UserProfile(cleanText(name), avatar)
    }

    override suspend fun guessYouLike(): List<Song> =
        if (isSignedIn) safe { songsFrom("/user/guess-you-like") } else emptyList()

    override suspend fun dailySongs(): List<Song> =
        if (isSignedIn) safe { songsFrom("/user/daily-songs") } else emptyList()

    override suspend fun recommendedSongs(): List<Song> {
        val daily = dailySongs()
        if (daily.isNotEmpty()) return daily
        return songsFrom("/recommend/new-songs")
    }

    override suspend fun recommendedPlaylists(): List<Collection> {
        val root = bridge.get("/recommend/playlists")
        return array(root, "playlists")?.mapNotNull { parsePlaylist(it, CollectionKind.Playlist) }?.take(12).orEmpty()
    }

    override suspend fun newAlbums(): List<Collection> {
        val root = bridge.get("/recommend/new-albums?limit=12")
        return array(root, "albums")?.mapNotNull { item ->
            val mid = str(item, "mid") ?: return@mapNotNull null
            val title = str(item, "name", "title") ?: return@mapNotNull null
            Collection(CollectionKind.Album, mid, cleanText(title), artistOf(item), albumCover(mid))
        }.orEmpty()
    }

    override suspend fun library(): List<Collection> {
        if (!isSignedIn) return emptyList()
        val result = mutableListOf<Collection>()
        array(bridge.get("/user/playlist"), "playlist")?.forEach { item ->
            parsePlaylist(item, CollectionKind.Owned)?.let(result::add)
        }
        result += safe {
            val array = firstObjectArray(bridge.get("/user/albums?offset=0&limit=60")) ?: return@safe emptyList()
            array.mapNotNull { item ->
                val mid = str(item, "albumMid", "albummid", "mid", "album_mid") ?: return@mapNotNull null
                val title = str(item, "albumName", "albumname", "name", "title") ?: return@mapNotNull null
                val artist = str(item, "singerName", "singername") ?: artistOf(item)
                Collection(CollectionKind.Album, mid, cleanText(title), artist, albumCover(mid))
            }
        }
        return result
    }

    override suspend fun liked(): List<Song> = if (isSignedIn) paged("/user/liked-songs?") else emptyList()

    override suspend fun setLiked(song: Song, liked: Boolean) {
        if (!isSignedIn) throw IllegalStateException("登入後才能收藏")
        if (song.kind != 0) throw IllegalStateException("這首歌無法收藏")
        val id = if (song.numericId > 0) song.numericId else resolveSongId(song)
        val body = buildJsonObject {
            putJsonObject("song") {
                put("id", id)
                put("mid", song.mid)
                put("type", 0)
            }
            put("liked", liked)
        }
        val root = bridge.postJson("/user/song/like", body.toString())
        if (int(root, "code") != 200) throw IllegalStateException("收藏失敗，請稍後再試")
    }

    private suspend fun resolveSongId(song: Song): Long {
        val root = bridge.get("/getSongInfo?songmid=${Uri.encode(song.mid)}")
        return deepObject(root, "track_info")?.let { long(it, "id") } ?: throw IllegalStateException("收藏失敗，請稍後再試")
    }

    override suspend fun songs(collection: Collection): List<Song> {
        val id = Uri.encode(collection.id)
        return when (collection.kind) {
            CollectionKind.Daily -> dailySongs()
            CollectionKind.Guess -> guessYouLike()
            CollectionKind.Liked -> liked()
            CollectionKind.Album -> songsFrom("/getAlbumInfo?albummid=$id")
            CollectionKind.Toplist -> songsFrom("/recommend/toplist?id=$id&limit=100")
            CollectionKind.Singer -> singerSongs(collection.id)
            CollectionKind.Owned -> safe { paged("/user/playlist-detail?tid=$id&") }.ifEmpty { songsFrom("/getSongListDetail/$id") }
            CollectionKind.Playlist -> songsFrom("/getSongListDetail/$id")
        }
    }

    override suspend fun search(keyword: String): List<Song> {
        val root = bridge.get("/search?keyword=${query(keyword)}&type=song&page=1&limit=30")
        val code = int(root, "code")
        if (code != null && code != 200) throw IllegalStateException(if (isSignedIn) "搜尋暫時無法使用" else "登入後才能搜尋")
        return findSongs(root)
    }

    override suspend fun searchCollections(keyword: String, kind: CollectionKind): List<Collection> {
        val type = if (kind == CollectionKind.Album) "album" else "playlist"
        val items = array(bridge.get("/search?keyword=${query(keyword)}&type=$type&page=1&limit=20"), "items") ?: return emptyList()
        return items.mapNotNull { item ->
            if (kind == CollectionKind.Album) {
                val mid = str(item, "albummid", "albumMid", "mid") ?: return@mapNotNull null
                val name = str(item, "name", "albumName", "title") ?: return@mapNotNull null
                val year = str(item, "publish_date", "publishDate")?.takeIf { it.length >= 4 }?.let { " · ${it.take(4)}" } ?: ""
                Collection(CollectionKind.Album, mid, cleanText(name), cleanText(str(item, "singer", "singerName") ?: "") + year, albumCover(mid))
            } else {
                val playlist = parsePlaylist(item, CollectionKind.Playlist) ?: return@mapNotNull null
                val owner = str(item, "nickname", "creator")?.let { " · ${cleanText(it)}" } ?: ""
                playlist.copy(subtitle = playlist.subtitle + owner)
            }
        }
    }

    override fun singerPage(artist: Artist) = Collection(CollectionKind.Singer, artist.mid, artist.name, "歌手", singerCover(artist.mid))

    override suspend fun searchSingers(keyword: String): List<Collection> {
        val found = mutableListOf<Collection>()
        val seen = mutableSetOf<String>()
        fun add(item: JsonElement) {
            val mid = str(item, "singerMID", "singerMid", "singer_mid", "singermid", "mid") ?: return
            val name = str(item, "singerName", "singer_name", "name", "title") ?: return
            if (!seen.add(mid)) return
            val count = int(item, "songNum", "song_num", "songnum")
            found += Collection(CollectionKind.Singer, mid, cleanText(name), if (count != null && count > 0) "$count 首" else "歌手", singerCover(mid))
        }
        val match = bridge.get("/search/match?keyword=${query(keyword)}")
        array(obj(obj(match, "data"), "singer"), "itemlist")?.forEach(::add)
        if (isSignedIn) {
            safe { array(bridge.get("/search?keyword=${query(keyword)}&type=singer&page=1&limit=12"), "items").orEmpty().toList() }
                .forEach(::add)
        }
        return found
    }

    override suspend fun stream(mid: String): String? {
        val order = when {
            !isSignedIn -> listOf("128")
            streamQuality == "320" -> listOf("320", "128")
            streamQuality == "128" -> listOf("128")
            else -> listOf("best", "320", "128")
        }
        for (quality in order) {
            val root = bridge.get("/getMusicPlay/${Uri.encode(mid)}?quality=$quality")
            val entry = obj(obj(obj(root, "data"), "playUrl"), mid)
            str(entry, "url")?.let { return it }
        }
        return null
    }

    override suspend fun lyrics(mid: String): List<LyricEntry> {
        val text = deep(bridge.get("/getLyric?songmid=${Uri.encode(mid)}"), "lyric")
        return if (text.isNullOrEmpty()) emptyList() else Lrc.parse(text)
    }

    override suspend fun createQr(channel: String): QrTicket {
        val key = deep(bridge.get("/login/qr/key?channel=$channel"), "unikey") ?: throw IllegalStateException("無法取得登入碼")
        val image = deep(bridge.get("/login/qr/create?key=${Uri.encode(key)}"), "qrimg") ?: throw IllegalStateException("無法取得登入碼")
        val data = image.substringAfter(',', image)
        return QrTicket(key, Base64.decode(data, Base64.DEFAULT))
    }

    override suspend fun checkQr(key: String): QrStatus {
        val root = bridge.get("/login/qr/check?key=${Uri.encode(key)}")
        val code = int(root, "code") ?: 800
        if (code == 803) {
            val token = str(root, "cookie")?.split(';')?.map { it.trim() }
                ?.firstOrNull { it.startsWith("qqmusic_session=") }
            if (token != null) bridge.saveToken(token.removePrefix("qqmusic_session="))
        }
        val raw = if (code == 800) root.toString() else ""
        val hint = when {
            "login.device-limit" in raw -> "登入裝置已達上限，請先到 QQ 音樂 App 移除舊裝置"
            "login.rate-limited" in raw -> "操作太頻繁，請稍後再試"
            "login.wrong-scan-app" in raw -> "請用對應的 App 掃碼"
            else -> ""
        }
        return QrStatus(code, hint)
    }

    override suspend fun signOut() {
        try {
            bridge.get("/logout")
        } catch (e: IOException) {
        } catch (e: SerializationException) {
        }
        bridge.clearToken()
    }

    private suspend fun singerSongs(mid: String): List<Song> {
        val songs = mutableListOf<Song>()
        val seen = mutableSetOf<String>()
        for (page in 1..5) {
            val batch = findSongs(bridge.get("/getSingerHotsong?singermid=${Uri.encode(mid)}&limit=60&page=$page"))
            batch.filterTo(songs) { seen.add(it.mid) }
            if (batch.size < 60) break
        }
        return songs
    }

    private suspend fun paged(path: String): List<Song> {
        val all = mutableListOf<Song>()
        var offset = 0
        while (offset < 3000) {
            val root = bridge.get("${path}offset=$offset&limit=100")
            val page = findSongs(root)
            all += page
            if (!isTrue(root, "more") || page.isEmpty()) break
            offset += 100
        }
        return all
    }

    private suspend fun songsFrom(path: String): List<Song> = findSongs(bridge.get(path))

    private suspend fun <T> safe(load: suspend () -> List<T>): List<T> = try {
        load()
    } catch (e: CancellationException) {
        throw e
    } catch (e: Exception) {
        emptyList()
    }

    private fun query(keyword: String) = Uri.encode(keyword.take(60))

    private fun parsePlaylist(item: JsonElement, kind: CollectionKind): Collection? {
        val id = str(item, "tid", "dissid", "id", "disstid") ?: return null
        val title = str(item, "dirName", "title", "dissname", "name") ?: return null
        if (id == "0") return null
        val count = int(item, "songNum", "songnum", "song_cnt", "songCnt")
        val cover = str(item, "bigpicUrl", "picUrl", "picurl", "logo", "albumPicUrl", "cover") ?: ""
        return Collection(kind, id, cleanText(title), if (count != null && count > 0) "$count 首" else "歌單", cover)
    }

    /** The API nests track arrays differently per route, so find the first array of tracks. */
    private fun findSongs(root: JsonElement): List<Song> {
        val queue = ArrayDeque<JsonElement>()
        queue.add(root)
        while (queue.isNotEmpty()) {
            when (val node = queue.removeFirst()) {
                is JsonArray -> {
                    val songs = node.mapNotNull(::parseSong)
                    if (songs.isNotEmpty()) return songs
                    queue.addAll(node)
                }
                is JsonObject -> node.values.filterTo(queue) { it is JsonArray || it is JsonObject }
                else -> Unit
            }
        }
        return emptyList()
    }

    private fun parseSong(raw: JsonElement): Song? {
        if (raw !is JsonObject) return null
        val item = obj(raw, "track") ?: obj(raw, "songInfo") ?: raw
        val mid = str(item, "mid", "songmid") ?: return null
        val title = str(item, "title", "name", "songname") ?: return null
        val singers = array(item, "singer") ?: return null
        var albumName = str(item, "albumname") ?: ""
        var albumMid = str(item, "albummid") ?: ""
        obj(item, "album")?.let { album ->
            albumName = str(album, "title", "name") ?: albumName
            albumMid = str(album, "mid", "pmid") ?: albumMid
        }
        return Song(
            mid = mid,
            title = cleanText(title),
            artist = artistOf(item),
            album = cleanText(albumName),
            albumMid = albumMid,
            seconds = int(item, "interval") ?: 0,
            numericId = long(item, "id", "songid") ?: 0,
            kind = int(item, "type", "songtype") ?: 0,
            singers = singers.mapNotNull { singer ->
                val singerMid = str(singer, "mid", "singermid") ?: return@mapNotNull null
                Artist(singerMid, cleanText(str(singer, "title", "name") ?: ""))
            },
        )
    }

    private fun artistOf(item: JsonElement): String {
        for (key in listOf("singer", "singers", "singerList")) {
            val list = array(item, key) ?: continue
            return list.mapNotNull { str(it, "title", "name") }.joinToString(" / ") { cleanText(it) }
        }
        return ""
    }
}
