package app.silk.playback

import android.net.Uri
import android.os.Bundle
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import app.silk.data.Song

/**
 * Songs travel through the player as media items whose URI is a placeholder (silk://song/<mid>).
 * The real stream address is fetched only when the player opens the item, because QQ Music links expire.
 */
object SongItems {
    const val SCHEME = "silk"

    fun uriFor(mid: String): Uri = Uri.parse("$SCHEME://song/${Uri.encode(mid)}")

    fun toItem(song: Song): MediaItem {
        val extras = Bundle().apply {
            putString("album", song.album)
            putString("albumMid", song.albumMid)
            putInt("seconds", song.seconds)
            putString("coverUrl", song.coverUrl)
            putLong("numericId", song.numericId)
            putInt("kind", song.kind)
            putString("singerMid", song.singerMid)
        }
        val metadata = MediaMetadata.Builder()
            .setTitle(song.title)
            .setArtist(song.artist)
            .setAlbumTitle(song.album)
            .setArtworkUri(song.cover.takeIf { it.isNotEmpty() }?.let(Uri::parse))
            .setDurationMs(song.seconds * 1000L)
            .setExtras(extras)
            .build()
        return MediaItem.Builder()
            .setMediaId(song.mid)
            .setUri(uriFor(song.mid))
            .setMediaMetadata(metadata)
            .build()
    }

    fun toSong(item: MediaItem): Song {
        val metadata = item.mediaMetadata
        val extras = metadata.extras ?: Bundle.EMPTY
        return Song(
            mid = item.mediaId,
            title = metadata.title?.toString().orEmpty(),
            artist = metadata.artist?.toString().orEmpty(),
            album = extras.getString("album").orEmpty(),
            albumMid = extras.getString("albumMid").orEmpty(),
            seconds = extras.getInt("seconds"),
            coverUrl = extras.getString("coverUrl"),
            numericId = extras.getLong("numericId"),
            kind = extras.getInt("kind"),
            singerMid = extras.getString("singerMid"),
        )
    }

    /** Items from a controller arrive without their URI; give it back. */
    fun restore(item: MediaItem): MediaItem = item.buildUpon().setUri(uriFor(item.mediaId)).build()
}
