package app.silk.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.silk.data.Collection
import app.silk.data.CollectionKind
import app.silk.data.Song

/** The header shared by inner pages: a back button and a title. */
@Composable
private fun PageHeader(title: String, onBack: () -> Unit, trailing: @Composable () -> Unit = {}) {
    val silk = LocalSilk.current
    Row(Modifier.fillMaxWidth().padding(horizontal = 10.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(44.dp).pressable(onBack).semantics { contentDescription = "返回" }, contentAlignment = Alignment.Center) {
            Icon(SilkIcons.Back, contentDescription = null, tint = silk.fg, modifier = Modifier.size(24.dp))
        }
        Text(title, color = silk.fg, fontSize = 19.sp, fontWeight = FontWeight.SemiBold, maxLines = 1,
            overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f).padding(start = 4.dp))
        trailing()
    }
}

@Composable
private fun SongList(vm: AppViewModel, songs: List<Song>, from: CollectionKind?, header: @Composable () -> Unit = {}) {
    val state = rememberLazyListState()
    LazyColumn(state = state, modifier = Modifier.edgeFade(state), contentPadding = PaddingValues(start = 10.dp, end = 10.dp, bottom = 24.dp)) {
        item { header() }
        itemsIndexed(songs, key = { i, song -> "$i:${song.mid}" }) { index, song ->
            SongRow(
                song = song,
                number = index + 1,
                isCurrent = vm.current?.mid == song.mid,
                isLiked = song.mid in vm.liked,
                onPlay = { vm.play(songs, index, from) },
                onPlayNext = { vm.playNext(song) },
                onAddToQueue = { vm.addToQueue(song) },
                onLike = { vm.toggleLike(song) },
                onSinger = vm::openSinger,
            )
        }
    }
}

@Composable
fun SongsScreen(vm: AppViewModel, collection: Collection) {
    val silk = LocalSilk.current
    val state = vm.lists[vm.keyOf(collection)] ?: SongListState()
    Column(Modifier.fillMaxSize()) {
        PageHeader(collection.title, onBack = { vm.back() })
        SongList(vm, state.songs, collection.kind) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                val cover = collection.cover.ifEmpty { state.songs.firstOrNull()?.cover.orEmpty() }
                Cover(cover, Modifier.size(92.dp), if (collection.kind == CollectionKind.Singer) CircleShape else RoundedCornerShape(20.dp))
                Spacer(Modifier.width(16.dp))
                Column(Modifier.weight(1f)) {
                    Text(collection.title, color = silk.fg, fontSize = 20.sp, fontWeight = FontWeight.SemiBold, maxLines = 2)
                    val subtitle = if (state.songs.isNotEmpty()) "${state.songs.size} 首" else collection.subtitle
                    Text(subtitle, color = silk.fg70, fontSize = 13.sp)
                    Spacer(Modifier.height(10.dp))
                    if (state.songs.isNotEmpty()) {
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Chip("播放全部", false) { vm.play(state.songs, 0, collection.kind) }
                            if (collection.kind == CollectionKind.Guess) Chip("換一批", false) { vm.loadSongs(collection) }
                        }
                    }
                }
            }
            if (state.loading) Message("正在載入")
            state.error?.let { Message(it) }
        }
    }
}

@Composable
fun LibraryScreen(vm: AppViewModel) {
    val state = vm.library
    Column(Modifier.fillMaxSize()) {
        PageHeader("音樂庫", onBack = { vm.back() })
        val grid = rememberLazyGridState()
        LazyVerticalGrid(
            state = grid,
            modifier = Modifier.edgeFade(grid),
            columns = GridCells.Adaptive(150.dp),
            contentPadding = PaddingValues(16.dp),
            horizontalArrangement = Arrangement.spacedBy(14.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp),
        ) {
            if (state.loading) item(span = { GridItemSpan(maxLineSpan) }) { Message("正在載入") }
            state.error?.let { item(span = { GridItemSpan(maxLineSpan) }) { Message(it) } }
            items(state.items) { item -> CollectionCard(item) { vm.openCollection(item) } }
        }
    }
}

@Composable
fun SearchScreen(vm: AppViewModel) {
    val silk = LocalSilk.current
    val search = vm.search
    val focus = remember { FocusRequester() }
    val grid = rememberLazyGridState()
    LaunchedEffect(Unit) { if (!search.searched) focus.requestFocus() }
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 10.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(44.dp).pressable { vm.back() }.semantics { contentDescription = "返回" }, contentAlignment = Alignment.Center) {
                Icon(SilkIcons.Back, contentDescription = null, tint = silk.fg, modifier = Modifier.size(24.dp))
            }
            Row(
                Modifier
                    .weight(1f)
                    .height(48.dp)
                    .clip(RoundedCornerShape(24.dp))
                    .background(silk.fg07)
                    .border(1.dp, silk.fg12, RoundedCornerShape(24.dp))
                    .padding(horizontal = 16.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(SilkIcons.Search, contentDescription = null, tint = silk.fg70, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(10.dp))
                Box(Modifier.weight(1f)) {
                    if (search.query.isEmpty()) Text("歌曲、歌手、專輯", color = silk.fg45, fontSize = 15.sp)
                    BasicTextField(
                        value = search.query,
                        onValueChange = vm::setQuery,
                        singleLine = true,
                        textStyle = TextStyle(color = silk.fg, fontSize = 15.sp),
                        cursorBrush = SolidColor(silk.fg),
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                        keyboardActions = KeyboardActions(onSearch = { vm.submitSearch() }),
                        modifier = Modifier.fillMaxWidth().focusRequester(focus).semantics { contentDescription = "搜尋" },
                    )
                }
                if (search.query.isNotEmpty()) {
                    Box(Modifier.size(32.dp).pressable { vm.setQuery("") }.semantics { contentDescription = "清除" }, contentAlignment = Alignment.Center) {
                        Icon(SilkIcons.Close, contentDescription = null, tint = silk.fg70, modifier = Modifier.size(16.dp))
                    }
                }
            }
            Spacer(Modifier.width(10.dp))
        }

        Row(Modifier.padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf("歌曲", "歌手", "專輯", "歌單").forEachIndexed { i, label -> Chip(label, search.tab == i) { vm.setSearchTab(i) } }
        }

        when {
            !search.searched -> SearchHistory(vm)
            search.loading -> Message("正在搜尋")
            search.error != null -> Message(search.error)
            search.tab == 0 -> if (search.songs.isEmpty()) Message("沒有找到相關歌曲") else SongList(vm, search.songs, null)
            search.collections.isEmpty() -> Message("沒有找到相關結果")
            else -> LazyVerticalGrid(
                state = grid,
                modifier = Modifier.edgeFade(grid),
                columns = GridCells.Adaptive(140.dp),
                contentPadding = PaddingValues(16.dp),
                horizontalArrangement = Arrangement.spacedBy(14.dp),
                verticalArrangement = Arrangement.spacedBy(18.dp),
            ) {
                items(search.collections) { item ->
                    CollectionCard(item, round = item.kind == CollectionKind.Singer) { vm.openCollection(item) }
                }
            }
        }
    }
}

@OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@Composable
private fun SearchHistory(vm: AppViewModel) {
    val silk = LocalSilk.current
    if (vm.history.isEmpty()) return
    Column(Modifier.padding(horizontal = 18.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 14.dp, bottom = 8.dp)) {
            Text("最近搜尋", color = silk.fg70, fontSize = 13.sp, modifier = Modifier.weight(1f))
            Text("清除", color = silk.fg70, fontSize = 13.sp, modifier = Modifier.clip(RoundedCornerShape(10.dp)).pressable { vm.clearHistory() }.padding(8.dp, 4.dp))
        }
        androidx.compose.foundation.layout.FlowRow(
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            vm.history.forEach { word -> Chip(word, false) { vm.submitSearch(word) } }
        }
    }
}

/** QR sign-in. On a phone the code is on the very screen that would scan it, so it can be saved to the gallery first. */
@Composable
fun LoginScreen(vm: AppViewModel) {
    val silk = LocalSilk.current
    val login = vm.login
    Column(Modifier.fillMaxSize()) {
        PageHeader("登入 QQ 音樂", onBack = { vm.back() })
        Column(Modifier.fillMaxWidth().padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf("qq" to "QQ", "wechat" to "微信", "qqmusic" to "QQ 音樂").forEach { (channel, label) ->
                    Chip(label, login.channel == channel) { vm.startLogin(channel) }
                }
            }
            Spacer(Modifier.height(24.dp))
            Box(
                Modifier.size(240.dp).clip(RoundedCornerShape(28.dp)).background(Color.White).padding(16.dp),
                contentAlignment = Alignment.Center,
            ) {
                val image = login.image
                if (image != null) {
                    Image(image, contentDescription = "登入碼", filterQuality = FilterQuality.None, modifier = Modifier.fillMaxSize())
                } else {
                    Text(if (login.busy) "正在取得登入碼" else "登入碼已失效", color = Color(0xFF555555), fontSize = 14.sp)
                }
            }
            Spacer(Modifier.height(18.dp))
            Text(login.hint, color = silk.fg70, fontSize = 13.5.sp, lineHeight = 20.sp, modifier = Modifier.padding(horizontal = 12.dp))
            Spacer(Modifier.height(18.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                if (login.image != null) Chip("儲存到相簿", false) { vm.saveLoginCode() }
                Chip("重新取得", false) { vm.startLogin(login.channel) }
            }
        }
    }
}
