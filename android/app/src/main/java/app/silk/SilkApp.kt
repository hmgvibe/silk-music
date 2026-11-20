package app.silk

import android.app.Application
import android.content.Context
import app.silk.bridge.Bridge
import app.silk.data.DemoSource
import app.silk.data.MusicSource
import app.silk.data.QqMusicSource
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

class SilkApp : Application() {
    lateinit var settings: Settings
        private set

    private val lock = Mutex()
    private var source: MusicSource? = null

    override fun onCreate() {
        super.onCreate()
        settings = Settings(getSharedPreferences("silk", Context.MODE_PRIVATE))
    }

    /** The music source, started on first use. Both the screens and the playback service share it. */
    suspend fun source(): MusicSource = lock.withLock {
        source ?: create().also { source = it }
    }

    private suspend fun create(): MusicSource =
        if (settings.demo) DemoSource(this)
        else QqMusicSource(Bridge.start(this)).also { it.streamQuality = settings.quality }
}

/** Preferences that survive restarts. */
class Settings(private val prefs: android.content.SharedPreferences) {
    var demo: Boolean
        get() = prefs.getBoolean("demo", false)
        set(value) = prefs.edit().putBoolean("demo", value).apply()

    var quality: String
        get() = prefs.getString("quality", "best") ?: "best"
        set(value) = prefs.edit().putString("quality", value).apply()

    var lyricSize: Float
        get() = prefs.getFloat("lyricSize", 26f)
        set(value) = prefs.edit().putFloat("lyricSize", value).apply()

    /** 0 left, 1 centre, 2 right. */
    var lyricAlign: Int
        get() = prefs.getInt("lyricAlign", 1)
        set(value) = prefs.edit().putInt("lyricAlign", value).apply()

    var lyricBlur: Boolean
        get() = prefs.getBoolean("lyricBlur", true)
        set(value) = prefs.edit().putBoolean("lyricBlur", value).apply()

    init {
        // An earlier build saved the shift per song; it now belongs to the output device.
        val stale = prefs.all.keys.filter { it.startsWith("lyricOffset:") }
        if (stale.isNotEmpty()) prefs.edit().apply { stale.forEach(::remove) }.apply()
    }

    /** Lyric shift in milliseconds for one output device; positive shows lyrics earlier. */
    fun lyricOffset(device: String) = prefs.getInt("deviceOffset:$device", 0)

    fun setLyricOffset(device: String, ms: Int) = prefs.edit().apply {
        if (ms == 0) remove("deviceOffset:$device") else putInt("deviceOffset:$device", ms)
    }.apply()

    var lyricBold: Boolean
        get() = prefs.getBoolean("lyricBold", true)
        set(value) = prefs.edit().putBoolean("lyricBold", value).apply()

    var searchHistory: List<String>
        get() = prefs.getString("searchHistory", "")!!.split('\n').filter { it.isNotBlank() }
        set(value) = prefs.edit().putString("searchHistory", value.take(12).joinToString("\n")).apply()
}
