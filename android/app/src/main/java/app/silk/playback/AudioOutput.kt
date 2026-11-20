package app.silk.playback

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioDeviceCallback
import android.media.AudioDeviceInfo
import android.media.AudioManager
import android.os.Build
import android.os.Handler
import android.os.Looper

/** Where music is playing: [key] identifies the device for saved settings, [label] is shown to the user. */
data class OutputDevice(val key: String, val label: String)

/**
 * Follows the phone's audio output (speaker, wired, a particular Bluetooth headset), so settings such as the
 * lyric shift that make up for Bluetooth delay can belong to the device rather than to a song.
 */
class AudioOutput(context: Context, private val onChange: (OutputDevice) -> Unit) {
    private val audio = context.getSystemService(AudioManager::class.java)
    private val callback = object : AudioDeviceCallback() {
        override fun onAudioDevicesAdded(addedDevices: Array<out AudioDeviceInfo>) = onChange(current())
        override fun onAudioDevicesRemoved(removedDevices: Array<out AudioDeviceInfo>) = onChange(current())
    }

    fun start() = audio.registerAudioDeviceCallback(callback, Handler(Looper.getMainLooper()))

    fun stop() = audio.unregisterAudioDeviceCallback(callback)

    fun current(): OutputDevice {
        // Android 13 can say exactly where media is routed; before that, take the device Android would prefer.
        val routed = if (Build.VERSION.SDK_INT >= 33) {
            runCatching {
                val music = AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                    .build()
                audio.getAudioDevicesForAttributes(music).firstOrNull()
            }.getOrNull()
        } else {
            null
        }
        val device = routed ?: audio.getDevices(AudioManager.GET_DEVICES_OUTPUTS).filter { rank(it) > 0 }.maxByOrNull(::rank)
        return describe(device)
    }

    private fun rank(device: AudioDeviceInfo) = when (device.type) {
        in BLUETOOTH -> 4
        AudioDeviceInfo.TYPE_WIRED_HEADSET, AudioDeviceInfo.TYPE_WIRED_HEADPHONES -> 3
        AudioDeviceInfo.TYPE_USB_HEADSET, AudioDeviceInfo.TYPE_USB_DEVICE -> 2
        AudioDeviceInfo.TYPE_BUILTIN_SPEAKER -> 1
        else -> 0
    }

    private fun describe(device: AudioDeviceInfo?): OutputDevice {
        if (device == null) return SPEAKER
        val name = device.productName?.toString()?.trim().orEmpty()
        return when (device.type) {
            in BLUETOOTH -> OutputDevice("bt:$name", name.ifEmpty { "藍牙耳機" })
            AudioDeviceInfo.TYPE_WIRED_HEADSET, AudioDeviceInfo.TYPE_WIRED_HEADPHONES -> OutputDevice("wired", "有線耳機")
            AudioDeviceInfo.TYPE_USB_HEADSET, AudioDeviceInfo.TYPE_USB_DEVICE -> OutputDevice("usb:$name", name.ifEmpty { "USB 音訊" })
            AudioDeviceInfo.TYPE_BUILTIN_SPEAKER, AudioDeviceInfo.TYPE_BUILTIN_EARPIECE -> SPEAKER
            else -> OutputDevice("type:${device.type}:$name", name.ifEmpty { "其他輸出" })
        }
    }

    companion object {
        val SPEAKER = OutputDevice("speaker", "手機喇叭")

        private val BLUETOOTH = buildSet {
            add(AudioDeviceInfo.TYPE_BLUETOOTH_A2DP)
            add(AudioDeviceInfo.TYPE_HEARING_AID)
            if (Build.VERSION.SDK_INT >= 31) {
                add(AudioDeviceInfo.TYPE_BLE_HEADSET)
                add(AudioDeviceInfo.TYPE_BLE_SPEAKER)
            }
        }
    }
}
