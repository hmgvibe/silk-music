package app.silk.bridge

import android.content.Context
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.ServerSocket
import java.net.URL
import java.security.SecureRandom
import java.util.zip.ZipInputStream

/**
 * The QQ Music service from the desktop app, running on Node inside this process.
 * It listens on 127.0.0.1 only and answers only requests that carry this launch's key.
 */
class Bridge private constructor(private val port: Int, private val key: String, private val store: SecureStore) {

    @Volatile
    var token: String? = store.read(SESSION)
        private set

    suspend fun get(path: String): JsonElement = request("GET", path, null)

    suspend fun postJson(path: String, json: String): JsonElement = request("POST", path, json)

    fun saveToken(value: String) {
        token = value
        store.write(SESSION, value)
    }

    fun clearToken() {
        token = null
        store.delete(SESSION)
    }

    private suspend fun request(method: String, path: String, body: String?): JsonElement = withContext(Dispatchers.IO) {
        val connection = URL("http://127.0.0.1:$port$path").openConnection() as HttpURLConnection
        try {
            connection.requestMethod = method
            connection.connectTimeout = 5_000
            connection.readTimeout = 25_000
            connection.setRequestProperty("X-Silk-Key", key)
            token?.let { connection.setRequestProperty("X-QQ-Session", it) }
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
            }
            val stream = if (connection.responseCode >= 400) connection.errorStream else connection.inputStream
            val text = stream?.use { it.readBytes().toString(Charsets.UTF_8) } ?: throw IOException("empty response")
            Json.parseToJsonElement(text)
        } finally {
            connection.disconnect()
        }
    }

    private fun isListening(): Boolean = try {
        val connection = URL("http://127.0.0.1:$port/").openConnection() as HttpURLConnection
        connection.connectTimeout = 500
        connection.readTimeout = 2_000
        connection.setRequestProperty("X-Silk-Key", key)
        try {
            connection.responseCode > 0
        } finally {
            connection.disconnect()
        }
    } catch (e: IOException) {
        false
    }

    companion object {
        private const val TAG = "SilkBridge"
        private const val SESSION = "session"
        private const val SECRET = "secret"
        private val lock = Mutex()

        @Volatile
        private var instance: Bridge? = null

        suspend fun start(context: Context): Bridge = lock.withLock {
            instance ?: launch(context.applicationContext).also { instance = it }
        }

        private suspend fun launch(context: Context): Bridge = withContext(Dispatchers.IO) {
            val project = unpack(context)
            val store = SecureStore(context)
            val secret = store.read(SECRET) ?: randomHex(32).also { store.write(SECRET, it) }
            val auth = File(context.filesDir, "auth").apply { mkdirs() }
            val port = ServerSocket(0).use { it.localPort }
            val key = randomHex(32)

            NodeRuntime.setEnv("SILK_BRIDGE_KEY", key)
            NodeRuntime.setEnv("SILK_PORT", port.toString())
            NodeRuntime.setEnv("QQ_SESSION_SECRET", secret)
            NodeRuntime.setEnv("QQ_AUTH_SESSION_PATH", File(auth, "sessions.json").path)
            NodeRuntime.setEnv("QQ_AUTH_STATE_PATH", File(auth, "device.json").path)
            NodeRuntime.setEnv("LOG_LEVEL", "silent")
            NodeRuntime.setEnv("HOME", context.filesDir.path)
            NodeRuntime.setEnv("TMPDIR", context.cacheDir.path)

            // Node needs a large stack; the default thread stack is too small for its startup.
            Thread(null, {
                val code = NodeRuntime.start(arrayOf("node", File(project, "mobile.cjs").path))
                Log.w(TAG, "Node exited with code $code")
            }, "silk-node", 16L shl 20).apply { isDaemon = true }.start()

            val bridge = Bridge(port, key, store)
            withTimeout(40_000) {
                while (!bridge.isListening()) delay(150)
            }
            bridge
        }

        /** Unpacks the bundled music service once per installed version of the app. */
        private fun unpack(context: Context): File {
            val target = File(context.filesDir, "nodejs-project")
            val stamp = File(target, ".stamp")
            val version = context.packageManager.getPackageInfo(context.packageName, 0).lastUpdateTime.toString()
            if (stamp.exists() && stamp.readText() == version) return target

            target.deleteRecursively()
            target.mkdirs()
            ZipInputStream(context.assets.open("nodejs-project.zip").buffered()).use { zip ->
                val root = target.canonicalPath + File.separator
                while (true) {
                    val entry = zip.nextEntry ?: break
                    val file = File(target, entry.name)
                    if (!file.canonicalPath.startsWith(root)) continue
                    if (entry.isDirectory) {
                        file.mkdirs()
                    } else {
                        file.parentFile?.mkdirs()
                        file.outputStream().use { zip.copyTo(it) }
                    }
                }
            }
            stamp.writeText(version)
            return target
        }

        private fun randomHex(bytes: Int): String {
            val data = ByteArray(bytes).also { SecureRandom().nextBytes(it) }
            return data.joinToString("") { "%02X".format(it) }
        }
    }
}
