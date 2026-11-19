package app.silk.playback

import android.net.Uri
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.ResolvingDataSource
import app.silk.SilkApp
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.runBlocking
import java.io.FileNotFoundException
import java.io.IOException
import java.util.concurrent.ConcurrentHashMap

/** Thrown when QQ Music gives no stream for a song. Not retried by the player. */
class UnplayableException(mid: String) : FileNotFoundException("no stream for $mid")

/** Turns silk://song/<mid> into the real stream address when the player opens an item. */
@UnstableApi
class StreamResolver(private val app: SilkApp) : ResolvingDataSource.Resolver {
    private data class Entry(val url: String, val at: Long)

    // Seeking reopens the stream; reuse a fresh address instead of asking the service again.
    private val cache = ConcurrentHashMap<String, Entry>()

    override fun resolveDataSpec(dataSpec: DataSpec): DataSpec {
        val uri = dataSpec.uri
        if (uri.scheme != SongItems.SCHEME) return dataSpec
        val mid = Uri.decode(uri.lastPathSegment ?: return dataSpec)
        val now = System.currentTimeMillis()
        cache[mid]?.takeIf { now - it.at < FRESH_MS }?.let { return dataSpec.withUri(Uri.parse(it.url)) }

        val url = try {
            runBlocking { app.source().stream(mid) }
        } catch (e: CancellationException) {
            throw IOException(e)
        } catch (e: IOException) {
            throw e
        } catch (e: Exception) {
            throw IOException(e)
        } ?: throw UnplayableException(mid)
        cache[mid] = Entry(url, now)
        return dataSpec.withUri(Uri.parse(url))
    }

    fun forget(mid: String) {
        cache.remove(mid)
    }

    private companion object {
        const val FRESH_MS = 5 * 60_000L
    }
}
