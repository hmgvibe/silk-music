package app.silk.ui

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.silk.data.Artist
import app.silk.data.Song
import coil.compose.AsyncImage

@Composable
fun Cover(url: String, modifier: Modifier = Modifier, shape: Shape = RoundedCornerShape(14.dp)) {
    val silk = LocalSilk.current
    Box(modifier.clip(shape).background(silk.fg12)) {
        if (url.isNotEmpty()) {
            AsyncImage(model = url, contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.matchParentSize())
        }
    }
}

/** A round outlined button, like the Windows app's player controls. */
@Composable
fun RoundButton(
    icon: ImageVector,
    label: String,
    onClick: () -> Unit,
    size: Dp = 48.dp,
    iconSize: Dp = size * .42f,
    filled: Boolean = false,
    outlined: Boolean = true,
    modifier: Modifier = Modifier,
) {
    val silk = LocalSilk.current
    Box(
        modifier
            .size(size)
            .pressable(onClick)
            .clip(CircleShape)
            .then(
                when {
                    filled -> Modifier.background(silk.fg)
                    outlined -> Modifier.border(1.4.dp, silk.fg45, CircleShape)
                    else -> Modifier
                },
            )
            .semantics { contentDescription = label },
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = null, tint = if (filled) silk.a else silk.fg, modifier = Modifier.size(iconSize))
    }
}

/** A rounded square button for the home page header. */
@Composable
fun SquareButton(icon: ImageVector, label: String, onClick: () -> Unit, modifier: Modifier = Modifier, size: Dp = 48.dp) {
    val silk = LocalSilk.current
    Box(
        modifier
            .size(size)
            .pressable(onClick)
            .clip(RoundedCornerShape(16.dp))
            .background(silk.fg07)
            .border(1.dp, silk.fg12, RoundedCornerShape(16.dp))
            .semantics { contentDescription = label },
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = null, tint = silk.fg, modifier = Modifier.size(size * .46f))
    }
}

@Composable
fun SectionTitle(text: String, action: String? = null, onAction: () -> Unit = {}) {
    val silk = LocalSilk.current
    Row(Modifier.fillMaxWidth().padding(top = 22.dp, bottom = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(text, color = silk.fg, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
        if (action != null) {
            Text(action, color = silk.fg70, fontSize = 13.sp, modifier = Modifier.clip(RoundedCornerShape(10.dp)).pressable(onAction).padding(8.dp, 4.dp))
        }
    }
}

/** One song in a list. A long press opens the song menu. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun SongRow(
    song: Song,
    number: Int?,
    isCurrent: Boolean,
    isLiked: Boolean,
    onPlay: () -> Unit,
    onPlayNext: () -> Unit,
    onAddToQueue: () -> Unit,
    onLike: () -> Unit,
    onSinger: (Artist) -> Unit = {},
) {
    val silk = LocalSilk.current
    var menu by remember { mutableStateOf(false) }
    Box {
        Row(
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(14.dp))
                .background(if (isCurrent) silk.fg07 else silk.fg07.copy(alpha = 0f))
                .combinedClickable(onClick = onPlay, onLongClick = { menu = true })
                .padding(horizontal = 8.dp, vertical = 7.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (number != null) {
                Text(
                    "$number", color = if (isCurrent) silk.fg else silk.fg45, fontSize = 13.sp,
                    modifier = Modifier.width(30.dp), maxLines = 1,
                )
            }
            Cover(song.cover, Modifier.size(46.dp), RoundedCornerShape(10.dp))
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.Center) {
                Text(
                    song.title, color = silk.fg, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                    fontWeight = if (isCurrent) FontWeight.SemiBold else FontWeight.Normal,
                )
                Text(song.artist, color = silk.fg70, fontSize = 12.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            if (isLiked) Icon(SilkIcons.HeartFilled, contentDescription = "已收藏", tint = silk.fg70, modifier = Modifier.size(15.dp))
            Box(
                Modifier.size(40.dp).clip(CircleShape).pressable { menu = true }.semantics { contentDescription = "更多" },
                contentAlignment = Alignment.Center,
            ) {
                Icon(SilkIcons.More, contentDescription = null, tint = silk.fg70, modifier = Modifier.size(20.dp))
            }
        }
        SongMenu(menu, onDismiss = { menu = false }, isLiked, onPlayNext, onAddToQueue, onLike, song.singers, onSinger)
    }
}

@Composable
fun SongMenu(
    open: Boolean,
    onDismiss: () -> Unit,
    isLiked: Boolean,
    onPlayNext: () -> Unit,
    onAddToQueue: () -> Unit,
    onLike: () -> Unit,
    singers: List<Artist> = emptyList(),
    onSinger: (Artist) -> Unit = {},
) {
    val silk = LocalSilk.current
    DropdownMenu(
        expanded = open, onDismissRequest = onDismiss,
        containerColor = silk.sheet, shape = RoundedCornerShape(16.dp),
    ) {
        DropdownMenuItem(text = { Text("下一首播放", color = silk.fg) }, onClick = { onDismiss(); onPlayNext() })
        DropdownMenuItem(text = { Text("加入播放清單", color = silk.fg) }, onClick = { onDismiss(); onAddToQueue() })
        DropdownMenuItem(text = { Text(if (isLiked) "取消收藏" else "收藏", color = silk.fg) }, onClick = { onDismiss(); onLike() })
        singers.take(4).forEach { singer ->
            DropdownMenuItem(
                text = { Text("歌手：${singer.name}", color = silk.fg, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                onClick = { onDismiss(); onSinger(singer) },
            )
        }
    }
}

/** A song's artist line that opens the singer's page; with several singers it asks which one. */
@Composable
fun ArtistLink(song: Song, onSinger: (Artist) -> Unit, content: @Composable () -> Unit) {
    val silk = LocalSilk.current
    var choosing by remember { mutableStateOf(false) }
    Box {
        Box(
            Modifier
                .clip(RoundedCornerShape(10.dp))
                .then(
                    if (song.singers.isEmpty()) Modifier
                    else Modifier.pressable {
                        if (song.singers.size == 1) onSinger(song.singers[0]) else choosing = true
                    },
                )
                .padding(horizontal = 8.dp, vertical = 3.dp),
        ) { content() }
        DropdownMenu(
            expanded = choosing, onDismissRequest = { choosing = false },
            containerColor = silk.sheet, shape = RoundedCornerShape(16.dp),
        ) {
            song.singers.forEach { singer ->
                DropdownMenuItem(
                    text = { Text(singer.name, color = silk.fg, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                    onClick = { choosing = false; onSinger(singer) },
                )
            }
        }
    }
}

@Composable
fun Message(text: String, modifier: Modifier = Modifier) {
    val silk = LocalSilk.current
    Box(modifier.fillMaxWidth().padding(vertical = 48.dp), contentAlignment = Alignment.Center) {
        Text(text, color = silk.fg70, fontSize = 14.sp)
    }
}
