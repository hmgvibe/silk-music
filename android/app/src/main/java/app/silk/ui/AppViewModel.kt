package app.silk.ui

import android.app.Application
import android.content.ComponentName
import android.content.ContentValues
import android.graphics.BitmapFactory
import android.graphics.drawable.BitmapDrawable
import android.provider.MediaStore
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.media3.common.MediaItem
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import app.silk.SilkApp
import app.silk.data.Collection
import app.silk.data.CollectionKind
import app.silk.data.LyricLine
import app.silk.data.Lrc
import app.silk.data.MusicSource
import app.silk.data.Song
import app.silk.data.UserProfile
import app.silk.playback.PlaybackService
import app.silk.playback.SongItems
import coil.imageLoader
import coil.request.ImageRequest
import coil.request.SuccessResult
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.guava.await
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

sealed interface Screen {
    data object Home : Screen
    data object Search : Screen
    data object Library : Screen
    data object Login : Screen
    data class Songs(val collection: Collection) : Screen
}

data class HomeState(
    val loading: Boolean = true,
    val guessCover: String = "",
    val dailyCover: String = "",
    val recommended: List<Song> = emptyList(),
    val playlists: List<Collection> = emptyList(),
    val albums: List<Collection> = emptyList(),
    val error: String? = null,
)

data class SongListState(val songs: List<Song> = emptyList(), val loading: Boolean = true, val error: String? = null)

data class SearchState(
    val query: String = "",
    val tab: Int = 0,
    val songs: List<Song> = emptyList(),
    val collections: List<Collection> = emptyList(),
    val loading: Boolean = false,
    val error: String? = null,
    val searched: Boolean = false,
)

data class LoginState(val channel: String = "qq", val image: ImageBitmap? = null, val hint: String = "", val busy: Boolean = false)

/** 0 play through the queue and wrap, 1 repeat one song, 2 shuffle — the Windows app's three modes. */
enum class PlayMode { Sequence, RepeatOne, Shuffle }

class AppViewModel(application: Application) : AndroidViewModel(application) {
    private val app = application as SilkApp
    private val settings = app.settings
    private var source: MusicSource? = null
    private var controller: MediaController? = null

    var screens by mutableStateOf(listOf<Screen>(Screen.Home))
        private set
    var playerOpen by mutableStateOf(false)
    var startError by mutableStateOf<String?>(null)
        private set
    var ready by mutableStateOf(false)
        private set

    var profile by mutableStateOf<UserProfile?>(null)
        private set
    var signedIn by mutableStateOf(false)
        private set
    var home by mutableStateOf(HomeState())
        private set
    val lists = mutableStateMapOf<String, SongListState>()
    var library by mutableStateOf<SongListStateOf<Collection>>(SongListStateOf())
        private set
    var search by mutableStateOf(SearchState())
        private set
    var history by mutableStateOf(settings.searchHistory)
        private set
    var login by mutableStateOf(LoginState())
        private set

    var palette by mutableStateOf(CoverPalette.Default)
        private set
    var current by mutableStateOf<Song?>(null)
        private set
    var isPlaying by mutableStateOf(false)
        private set
    var queue by mutableStateOf<List<Song>>(emptyList())
        private set
    var queueIndex by mutableIntStateOf(-1)
        private set
    var durationMs by mutableLongStateOf(0L)
        private set
    var mode by mutableStateOf(PlayMode.Sequence)
        private set
    var lyrics by mutableStateOf<List<LyricLine>>(emptyList())
        private set
    var lyricsNote by mutableStateOf<String?>(null)
        private set
    var liked by mutableStateOf<Set<String>>(emptySet())
        private set

    var lyricSize by mutableStateOf(settings.lyricSize)
        private set
    var lyricAlign by mutableIntStateOf(settings.lyricAlign)
        private set
    var lyricBlur by mutableStateOf(settings.lyricBlur)
        private set
    var lyricBold by mutableStateOf(settings.lyricBold)
        private set
    var quality by mutableStateOf(settings.quality)
        private set

    private val _toasts = MutableSharedFlow<String>(extraBufferCapacity = 4)
    val toasts = _toasts.asSharedFlow()

    private var origin: CollectionKind? = null
    private var lyricsJob: Job? = null
    private var paletteJob: Job? = null
    private var loginJob: Job? = null
    private var searchJob: Job? = null

    init {
        viewModelScope.launch { start() }
    }

    private suspend fun start() {
        startError = null
        val music = try {
            app.source()
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            startError = "音樂服務無法啟動，請再試一次。"
            return
        }
        source = music
        connect()
        ready = true
        refreshAccount()
    }

    fun retryStart() {
        viewModelScope.launch { start() }
    }

    private suspend fun connect() {
        val context = getApplication<Application>()
        val token = SessionToken(context, ComponentName(context, PlaybackService::class.java))
        val connected = MediaController.Builder(context, token).buildAsync().await()
        controller = connected
        connected.addListener(object : Player.Listener {
            override fun onEvents(player: Player, events: Player.Events) = sync()

            override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
                onTrackChanged()
            }

            override fun onPlayerError(error: PlaybackException) {
                toast("這首目前無法播放")
            }
        })
        syncQueue()
        sync()
        onTrackChanged()
    }

    private suspend fun refreshAccount() {
        val music = source ?: return
        profile = runCatching { music.profile() }.getOrNull()
        signedIn = music.isSignedIn
        loadHome()
        if (signedIn) {
            viewModelScope.launch {
                liked = runCatching { music.liked().map { it.mid }.toSet() }.getOrDefault(emptySet())
            }
        }
    }

    // ---------------------------------------------------------------- pages

    fun open(screen: Screen) {
        if (screen == Screen.Login && login.image == null) startLogin(login.channel)
        if (screen == Screen.Library) loadLibrary()
        screens = screens + screen
    }

    fun back(): Boolean {
        if (playerOpen) {
            playerOpen = false
            return true
        }
        if (screens.size <= 1) return false
        val leaving = screens.last()
        screens = screens.dropLast(1)
        if (leaving == Screen.Login) stopLogin()
        return true
    }

    fun loadHome() {
        val music = source ?: return
        viewModelScope.launch {
            home = home.copy(loading = true, error = null)
            try {
                val guess = async { runCatching { music.guessYouLike() }.getOrDefault(emptyList()) }
                val daily = async { runCatching { music.dailySongs() }.getOrDefault(emptyList()) }
                val recommended = async { runCatching { music.recommendedSongs() }.getOrDefault(emptyList()) }
                val playlists = async { runCatching { music.recommendedPlaylists() }.getOrDefault(emptyList()) }
                val albums = async { runCatching { music.newAlbums() }.getOrDefault(emptyList()) }
                val dailySongs = daily.await()
                if (dailySongs.isNotEmpty()) lists[keyOf(DAILY)] = SongListState(dailySongs, loading = false)
                home = HomeState(
                    loading = false,
                    guessCover = guess.await().firstOrNull()?.cover.orEmpty(),
                    dailyCover = dailySongs.firstOrNull()?.cover.orEmpty(),
                    recommended = recommended.await(),
                    playlists = playlists.await(),
                    albums = albums.await(),
                )
                if (home.recommended.isEmpty() && home.playlists.isEmpty()) home = home.copy(error = "暫時無法取得內容，請稍後再試。")
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                home = home.copy(loading = false, error = "暫時無法取得內容，請稍後再試。")
            }
        }
    }

    fun openCollection(collection: Collection) {
        if (!signedIn && collection.kind in setOf(CollectionKind.Daily, CollectionKind.Guess, CollectionKind.Liked)) {
            open(Screen.Login)
            return
        }
        open(Screen.Songs(collection))
        val key = keyOf(collection)
        if (lists[key]?.songs?.isNotEmpty() == true && collection.kind != CollectionKind.Guess) return
        loadSongs(collection)
    }

    fun loadSongs(collection: Collection) {
        val music = source ?: return
        val key = keyOf(collection)
        lists[key] = SongListState(loading = true)
        viewModelScope.launch {
            lists[key] = try {
                val songs = music.songs(collection)
                SongListState(songs, loading = false, error = if (songs.isEmpty()) "這裡還沒有歌曲" else null)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                SongListState(loading = false, error = e.message ?: "載入失敗，請稍後再試")
            }
        }
    }

    fun keyOf(collection: Collection) = "${collection.kind}:${collection.id}"

    private fun loadLibrary() {
        val music = source ?: return
        library = SongListStateOf(loading = true)
        viewModelScope.launch {
            library = try {
                val items = music.library()
                SongListStateOf(items, loading = false, error = if (items.isEmpty()) "登入後，這裡會顯示你的歌單與專輯" else null)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                SongListStateOf(loading = false, error = "載入失敗，請稍後再試")
            }
        }
    }

    // ---------------------------------------------------------------- search

    fun setQuery(text: String) {
        search = search.copy(query = text)
    }

    fun submitSearch(text: String = search.query) {
        val keyword = text.trim()
        if (keyword.isEmpty()) return
        history = (listOf(keyword) + history.filter { it != keyword }).take(12)
        settings.searchHistory = history
        search = search.copy(query = keyword)
        runSearch()
    }

    fun setSearchTab(tab: Int) {
        if (tab == search.tab) return
        search = search.copy(tab = tab, songs = emptyList(), collections = emptyList())
        if (search.query.isNotBlank() && search.searched) runSearch()
    }

    fun clearHistory() {
        history = emptyList()
        settings.searchHistory = emptyList()
    }

    private fun runSearch() {
        val music = source ?: return
        val keyword = search.query.trim()
        val tab = search.tab
        searchJob?.cancel()
        search = search.copy(loading = true, error = null, searched = true)
        searchJob = viewModelScope.launch {
            try {
                search = when (tab) {
                    0 -> search.copy(songs = music.search(keyword), loading = false)
                    1 -> search.copy(collections = music.searchSingers(keyword), loading = false)
                    2 -> search.copy(collections = music.searchCollections(keyword, CollectionKind.Album), loading = false)
                    else -> search.copy(collections = music.searchCollections(keyword, CollectionKind.Playlist), loading = false)
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                search = search.copy(loading = false, error = e.message ?: "搜尋暫時無法使用")
            }
        }
    }

    // ---------------------------------------------------------------- login

    fun startLogin(channel: String) {
        val music = source ?: return
        stopLogin()
        login = LoginState(channel = channel, busy = true, hint = "正在取得登入碼")
        loginJob = viewModelScope.launch {
            try {
                val ticket = music.createQr(channel)
                val bitmap = withContext(Dispatchers.Default) { BitmapFactory.decodeByteArray(ticket.image, 0, ticket.image.size) }
                login = login.copy(image = bitmap?.asImageBitmap(), busy = false, hint = scanHint(channel))
                while (isActive) {
                    delay(2000)
                    val status = runCatching { music.checkQr(ticket.key) }.getOrNull() ?: continue
                    when (status.code) {
                        803 -> {
                            login = login.copy(hint = "登入成功")
                            screens = screens.filter { it != Screen.Login }.ifEmpty { listOf(Screen.Home) }
                            login = LoginState(channel = channel)
                            refreshAccount()
                            return@launch
                        }
                        802 -> login = login.copy(hint = "已掃碼，請在手機上確認")
                        800 -> {
                            login = login.copy(hint = status.message.ifEmpty { "登入碼已過期，請重新取得" }, image = null)
                            return@launch
                        }
                    }
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                login = login.copy(busy = false, hint = e.message ?: "無法取得登入碼")
            }
        }
    }

    private fun scanHint(channel: String) = when (channel) {
        "wechat" -> "用微信掃碼登入"
        "qqmusic" -> "用 QQ 音樂 App 掃碼登入"
        else -> "用 QQ 掃碼登入"
    } + "。在這部手機上，可以先儲存登入碼，再從相簿識別。"

    private fun stopLogin() {
        loginJob?.cancel()
        loginJob = null
    }

    /** Saves the QR code to Pictures/絲音 so it can be scanned from the gallery on this same phone. */
    fun saveLoginCode() {
        val image = login.image ?: return
        val context = getApplication<Application>()
        viewModelScope.launch(Dispatchers.IO) {
            val values = ContentValues().apply {
                put(MediaStore.Images.Media.DISPLAY_NAME, "silk-login-${System.currentTimeMillis()}.png")
                put(MediaStore.Images.Media.MIME_TYPE, "image/png")
                put(MediaStore.Images.Media.RELATIVE_PATH, "Pictures/絲音")
            }
            val uri = context.contentResolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values)
            val ok = uri != null && context.contentResolver.openOutputStream(uri)?.use { out ->
                image.asAndroidBitmap().compress(android.graphics.Bitmap.CompressFormat.PNG, 100, out)
            } == true
            toast(if (ok) "已儲存到相簿的「絲音」資料夾" else "無法儲存登入碼")
        }
    }

    fun signOut() {
        val music = source ?: return
        viewModelScope.launch {
            music.signOut()
            liked = emptySet()
            lists.clear()
            refreshAccount()
        }
    }

    fun setStreamQuality(value: String) {
        quality = value
        settings.quality = value
        source?.streamQuality = value
    }

    // ---------------------------------------------------------------- playback

    fun play(songs: List<Song>, index: Int, from: CollectionKind? = null) {
        val player = controller ?: return
        if (songs.isEmpty()) return
        origin = from
        player.setMediaItems(songs.map(SongItems::toItem), index.coerceIn(songs.indices), 0)
        player.prepare()
        player.play()
    }

    fun toggle() {
        val player = controller ?: return
        if (player.mediaItemCount == 0) return
        if (player.isPlaying) {
            player.pause()
        } else {
            if (player.playbackState == Player.STATE_IDLE) player.prepare()
            if (player.playbackState == Player.STATE_ENDED) player.seekToDefaultPosition()
            player.play()
        }
    }

    fun next() = controller?.seekToNext() ?: Unit

    fun previous() = controller?.seekToPrevious() ?: Unit

    fun seekTo(ms: Long) = controller?.seekTo(ms) ?: Unit

    fun skipTo(index: Int) {
        val player = controller ?: return
        player.seekTo(index, 0)
        if (player.playbackState == Player.STATE_IDLE) player.prepare()
        player.play()
    }

    /** Current position, read every frame by the player and lyrics. */
    fun positionMs(): Long = controller?.currentPosition ?: 0L

    fun cycleMode() {
        val player = controller ?: return
        when (mode) {
            PlayMode.Sequence -> {
                player.repeatMode = Player.REPEAT_MODE_ONE
                player.shuffleModeEnabled = false
            }
            PlayMode.RepeatOne -> {
                player.repeatMode = Player.REPEAT_MODE_ALL
                player.shuffleModeEnabled = true
            }
            PlayMode.Shuffle -> {
                player.repeatMode = Player.REPEAT_MODE_ALL
                player.shuffleModeEnabled = false
            }
        }
    }

    fun playNext(song: Song) {
        val player = controller ?: return
        if (player.mediaItemCount == 0) return play(listOf(song), 0)
        val existing = queue.indexOfFirst { it.mid == song.mid }
        if (existing == queueIndex) return
        val target = player.currentMediaItemIndex + 1
        if (existing >= 0) player.moveMediaItem(existing, if (existing < target) target - 1 else target)
        else player.addMediaItem(target, SongItems.toItem(song))
        toast("將在下一首播放")
    }

    fun addToQueue(song: Song) {
        val player = controller ?: return
        if (player.mediaItemCount == 0) return play(listOf(song), 0)
        if (queue.any { it.mid == song.mid }) return toast("已在播放清單中")
        player.addMediaItem(SongItems.toItem(song))
        toast("已加入播放清單")
    }

    fun removeFromQueue(index: Int) {
        controller?.removeMediaItem(index)
    }

    fun moveInQueue(from: Int, to: Int) {
        if (from == to) return
        controller?.moveMediaItem(from, to)
    }

    fun clearQueue() {
        val player = controller ?: return
        val keep = player.currentMediaItemIndex
        if (keep < 0) return player.clearMediaItems()
        if (keep + 1 < player.mediaItemCount) player.removeMediaItems(keep + 1, player.mediaItemCount)
        if (keep > 0) player.removeMediaItems(0, keep)
    }

    fun toggleLike(song: Song) {
        val music = source ?: return
        if (!signedIn) return open(Screen.Login)
        val wasLiked = song.mid in liked
        liked = if (wasLiked) liked - song.mid else liked + song.mid
        viewModelScope.launch {
            try {
                music.setLiked(song, !wasLiked)
                lists.remove(keyOf(LIKED))
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                liked = if (wasLiked) liked + song.mid else liked - song.mid
                toast(e.message ?: "收藏失敗，請稍後再試")
            }
        }
    }

    // ---------------------------------------------------------------- lyric options

    fun changeLyricSize(value: Float) {
        lyricSize = value
        settings.lyricSize = value
    }

    fun changeLyricAlign(value: Int) {
        lyricAlign = value
        settings.lyricAlign = value
    }

    fun changeLyricBlur(value: Boolean) {
        lyricBlur = value
        settings.lyricBlur = value
    }

    fun changeLyricBold(value: Boolean) {
        lyricBold = value
        settings.lyricBold = value
    }

    // ---------------------------------------------------------------- sync

    private fun sync() {
        val player = controller ?: return
        isPlaying = player.isPlaying
        durationMs = player.duration.takeIf { it > 0 } ?: ((current?.seconds ?: 0) * 1000L)
        mode = when {
            player.shuffleModeEnabled -> PlayMode.Shuffle
            player.repeatMode == Player.REPEAT_MODE_ONE -> PlayMode.RepeatOne
            else -> PlayMode.Sequence
        }
        if (player.repeatMode == Player.REPEAT_MODE_OFF) player.repeatMode = Player.REPEAT_MODE_ALL
        if (queue.size != player.mediaItemCount || queueIndex != player.currentMediaItemIndex) syncQueue()
    }

    private fun syncQueue() {
        val player = controller ?: return
        queue = (0 until player.mediaItemCount).map { SongItems.toSong(player.getMediaItemAt(it)) }
        queueIndex = player.currentMediaItemIndex
    }

    private fun onTrackChanged() {
        val player = controller ?: return
        syncQueue()
        val song = player.currentMediaItem?.let(SongItems::toSong)
        if (song?.mid == current?.mid) return
        current = song
        if (song == null) return
        loadLyrics(song)
        loadPalette(song)
        extendRadio()
    }

    /** Guess-you-like plays like a radio: more songs are fetched as the queue runs out. */
    private fun extendRadio() {
        val music = source ?: return
        val player = controller ?: return
        if (origin != CollectionKind.Guess || player.currentMediaItemIndex < player.mediaItemCount - 2) return
        viewModelScope.launch {
            val known = queue.map { it.mid }.toSet()
            val more = runCatching { music.guessYouLike() }.getOrDefault(emptyList()).filter { it.mid !in known }
            if (more.isNotEmpty()) controller?.addMediaItems(more.map(SongItems::toItem))
        }
    }

    private fun loadLyrics(song: Song) {
        val music = source ?: return
        lyricsJob?.cancel()
        lyrics = emptyList()
        lyricsNote = null
        lyricsJob = viewModelScope.launch {
            try {
                val entries = music.lyrics(song.mid)
                val lines = Lrc.lines(entries)
                lyrics = lines
                if (lines.isEmpty()) lyricsNote = "這首歌沒有歌詞"
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                lyricsNote = "暫時無法取得歌詞"
            }
        }
    }

    private fun loadPalette(song: Song) {
        paletteJob?.cancel()
        if (song.cover.isEmpty()) return
        val context = getApplication<Application>()
        paletteJob = viewModelScope.launch {
            val request = ImageRequest.Builder(context).data(song.cover).allowHardware(false).size(96).build()
            val result = context.imageLoader.execute(request) as? SuccessResult ?: return@launch
            val bitmap = (result.drawable as? BitmapDrawable)?.bitmap ?: return@launch
            palette = withContext(Dispatchers.Default) { CoverPalette.from(bitmap) }
        }
    }

    fun toast(message: String) {
        _toasts.tryEmit(message)
    }

    override fun onCleared() {
        controller?.release()
        controller = null
        super.onCleared()
    }

    companion object {
        val GUESS = Collection(CollectionKind.Guess, "guess", "猜你喜歡", "跟著你的口味", "")
        val DAILY = Collection(CollectionKind.Daily, "daily", "每日 30 首", "每天更新", "")
        val LIKED = Collection(CollectionKind.Liked, "liked", "我喜歡", "你收藏的歌", "")
    }
}

data class SongListStateOf<T>(val items: List<T> = emptyList(), val loading: Boolean = false, val error: String? = null)
