package app.silk.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import app.silk.data.Song

/** QQ Music allows playlist names of up to 20 characters. */
private const val NAME_LIMIT = 20

/** "加入歌單": the user's own playlists, and a new one. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PlaylistPickerSheet(vm: AppViewModel, song: Song) {
    val silk = LocalSilk.current
    val list = rememberLazyListState()
    ModalBottomSheet(onDismissRequest = vm::closePlaylistPick, containerColor = silk.sheet, contentColor = silk.fg) {
        Column(Modifier.padding(horizontal = 22.dp)) {
            Text("加入歌單", fontSize = 20.sp, fontWeight = FontWeight.SemiBold, color = silk.fg)
            Text(song.title, fontSize = 13.sp, color = silk.fg70, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Spacer(Modifier.height(10.dp))
        LazyColumn(
            state = list,
            modifier = Modifier.heightIn(max = 480.dp).edgeFade(list),
            contentPadding = PaddingValues(start = 12.dp, end = 12.dp, bottom = 28.dp),
        ) {
            item {
                PickerRow(icon = true, title = "新建歌單", subtitle = "建立後加入這首歌") { vm.newPlaylist(song) }
            }
            val playlists = vm.pickablePlaylists
            when {
                playlists == null -> item { Message("正在載入") }
                playlists.isEmpty() -> item { Message("還沒有自己建立的歌單") }
                else -> items(playlists, key = { it.id }) { playlist ->
                    PickerRow(cover = playlist.cover, title = playlist.title, subtitle = playlist.subtitle) { vm.addToPlaylist(playlist, song) }
                }
            }
        }
    }
}

@Composable
private fun PickerRow(title: String, subtitle: String, cover: String = "", icon: Boolean = false, onClick: () -> Unit) {
    val silk = LocalSilk.current
    Row(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .pressable(onClick)
            .padding(horizontal = 8.dp, vertical = 7.dp)
            .semantics { contentDescription = title },
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon) {
            Box(
                Modifier.size(46.dp).clip(RoundedCornerShape(10.dp)).background(silk.fg07).border(1.dp, silk.fg12, RoundedCornerShape(10.dp)),
                contentAlignment = Alignment.Center,
            ) {
                Text("＋", color = silk.fg, fontSize = 22.sp)
            }
        } else {
            Cover(cover, Modifier.size(46.dp), RoundedCornerShape(10.dp))
        }
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            Text(title, color = silk.fg, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(subtitle, color = silk.fg70, fontSize = 12.5.sp, maxLines = 1)
        }
    }
}

/** The name for a new playlist. */
@Composable
fun PlaylistNameDialog(vm: AppViewModel, naming: PlaylistNaming) {
    val silk = LocalSilk.current
    var name by remember { mutableStateOf("") }
    val focus = remember { FocusRequester() }
    LaunchedEffect(Unit) { focus.requestFocus() }
    val ready = name.isNotBlank()
    Dialog(onDismissRequest = vm::closePlaylistNaming) {
        Column(
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(26.dp))
                .background(silk.sheet)
                .padding(start = 24.dp, end = 24.dp, top = 22.dp, bottom = 18.dp),
        ) {
            Text("新建歌單", color = silk.fg, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
            Text(
                naming.song?.let { "建立後加入「${it.title}」" } ?: "建立後會出現在音樂庫",
                color = silk.fg70, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 4.dp, bottom = 16.dp),
            )
            Box(
                Modifier
                    .fillMaxWidth()
                    .height(48.dp)
                    .clip(RoundedCornerShape(24.dp))
                    .background(silk.fg07)
                    .border(1.dp, silk.fg24, RoundedCornerShape(24.dp))
                    .padding(horizontal = 18.dp),
                contentAlignment = Alignment.CenterStart,
            ) {
                if (name.isEmpty()) Text("歌單名稱", color = silk.fg45, fontSize = 15.sp)
                BasicTextField(
                    value = name,
                    onValueChange = { if (it.codePointCount(0, it.length) <= NAME_LIMIT) name = it },
                    singleLine = true,
                    textStyle = TextStyle(color = silk.fg, fontSize = 15.sp),
                    cursorBrush = SolidColor(silk.fg),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { if (ready) vm.createPlaylist(name) }),
                    modifier = Modifier.fillMaxWidth().focusRequester(focus).semantics { contentDescription = "歌單名稱" },
                )
            }
            Row(Modifier.fillMaxWidth().padding(top = 16.dp), verticalAlignment = Alignment.CenterVertically) {
                Text("${name.codePointCount(0, name.length)} / $NAME_LIMIT", color = silk.fg45, fontSize = 12.sp, modifier = Modifier.weight(1f))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Chip("取消", false) { vm.closePlaylistNaming() }
                    // Highlighted once there is a name; empty names do nothing.
                    Chip("建立", ready) { if (ready) vm.createPlaylist(name) }
                }
            }
        }
    }
}
