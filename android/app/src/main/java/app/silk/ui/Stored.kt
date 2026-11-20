package app.silk.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import app.silk.Settings
import kotlin.reflect.KProperty

/** An Int setting that the UI observes and that is saved as soon as it changes. */
class StoredInt(private val settings: Settings, private val key: String, default: Int) {
    private var value by mutableIntStateOf(settings.int(key, default))

    operator fun getValue(thisRef: Any?, property: KProperty<*>) = value

    operator fun setValue(thisRef: Any?, property: KProperty<*>, newValue: Int) {
        value = newValue
        settings.putInt(key, newValue)
    }
}

/** A Boolean setting that the UI observes and that is saved as soon as it changes. */
class StoredFlag(private val settings: Settings, private val key: String, default: Boolean) {
    private var value by mutableStateOf(settings.flag(key, default))

    operator fun getValue(thisRef: Any?, property: KProperty<*>) = value

    operator fun setValue(thisRef: Any?, property: KProperty<*>, newValue: Boolean) {
        value = newValue
        settings.putFlag(key, newValue)
    }
}

/** A Float setting that the UI observes and that is saved as soon as it changes. */
class StoredFloat(private val settings: Settings, private val key: String, default: Float) {
    private var value by mutableFloatStateOf(settings.float(key, default))

    operator fun getValue(thisRef: Any?, property: KProperty<*>) = value

    operator fun setValue(thisRef: Any?, property: KProperty<*>, newValue: Float) {
        value = newValue
        settings.putFloat(key, newValue)
    }
}

/**
 * How the player looks in one orientation: the lyric sheet's options and the cover size.
 * Upright and sideways each keep their own; upright uses the original keys, so earlier choices carry over.
 */
class PlayerLook(settings: Settings, suffix: String) {
    var lyricSize by StoredFloat(settings, "lyricSize$suffix", DEFAULT_SIZE)
    /** 0 left, 1 centre, 2 right. */
    var lyricAlign by StoredInt(settings, "lyricAlign$suffix", 1)
    var lyricBlur by StoredFlag(settings, "lyricBlur$suffix", true)
    var lyricBold by StoredFlag(settings, "lyricBold$suffix", true)
    /** The sung line's size, in percent of the others. */
    var lyricScale by StoredInt(settings, "lyricScale$suffix", DEFAULT_SCALE)
    /** Line pitch in tenths of the font size. */
    var lyricSpacing by StoredInt(settings, "lyricSpacing$suffix", DEFAULT_SPACING)
    /** Brightness of lines other than the sung one, in percent. */
    var lyricDim by StoredInt(settings, "lyricDim$suffix", DEFAULT_DIM)
    /** Player cover: 0 small, 1 standard, 2 large. */
    var coverSize by StoredInt(settings, "coverSize$suffix", 1)

    fun reset() {
        lyricSize = DEFAULT_SIZE
        lyricAlign = 1
        lyricBlur = true
        lyricBold = true
        lyricScale = DEFAULT_SCALE
        lyricSpacing = DEFAULT_SPACING
        lyricDim = DEFAULT_DIM
        coverSize = 1
    }

    companion object {
        const val DEFAULT_SIZE = 26f
        const val DEFAULT_SCALE = 106
        const val DEFAULT_SPACING = 20
        const val DEFAULT_DIM = 38
    }
}
