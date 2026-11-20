package app.silk.data

data class Song(
    val mid: String,
    val title: String,
    val artist: String,
    val album: String = "",
    val albumMid: String = "",
    val seconds: Int = 0,
    val coverUrl: String? = null,
    val numericId: Long = 0,
    val kind: Int = 0,
    val singers: List<Artist> = emptyList(),
) {
    val cover: String
        get() = coverUrl ?: if (albumMid.isEmpty()) "" else albumCover(albumMid)
}

data class Artist(val mid: String, val name: String)

enum class CollectionKind { Daily, Guess, Liked, Owned, Playlist, Album, Toplist, Singer }

data class Collection(
    val kind: CollectionKind,
    val id: String,
    val title: String,
    val subtitle: String,
    val cover: String,
)

data class UserProfile(val name: String, val avatarUrl: String)

data class LyricEntry(val time: Double, val text: String)

class QrTicket(val key: String, val image: ByteArray)

data class QrStatus(val code: Int, val message: String)

fun albumCover(mid: String) = "https://y.gtimg.cn/music/photo_new/T002R500x500M000$mid.jpg"

fun singerCover(mid: String) = "https://y.gtimg.cn/music/photo_new/T001R300x300M000$mid.jpg"

fun formatTime(seconds: Double): String {
    val total = seconds.toInt().coerceAtLeast(0)
    return "%d:%02d".format(total / 60, total % 60)
}

/** Everything the app reads from QQ Music, or from the offline demo. */
interface MusicSource {
    val isSignedIn: Boolean
    var streamQuality: String

    suspend fun profile(): UserProfile?
    suspend fun guessYouLike(): List<Song>
    suspend fun dailySongs(): List<Song>
    suspend fun recommendedSongs(): List<Song>
    suspend fun recommendedPlaylists(): List<Collection>
    suspend fun newAlbums(): List<Collection>
    suspend fun library(): List<Collection>
    suspend fun liked(): List<Song>
    suspend fun setLiked(song: Song, liked: Boolean)
    suspend fun songs(collection: Collection): List<Song>
    suspend fun search(keyword: String): List<Song>
    suspend fun searchCollections(keyword: String, kind: CollectionKind): List<Collection>
    suspend fun searchSingers(keyword: String): List<Collection>
    /** The page for one of a song's singers. */
    fun singerPage(artist: Artist): Collection
    suspend fun stream(mid: String): String?
    suspend fun lyrics(mid: String): List<LyricEntry>
    suspend fun createQr(channel: String): QrTicket
    suspend fun checkQr(key: String): QrStatus
    suspend fun signOut()
}
