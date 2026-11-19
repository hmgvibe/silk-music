package app.silk.ui

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.silk.data.formatTime

/** Two pages, as in the sketch: the cover with its controls, then the lyrics. Dots at the top show which page is open. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun PlayerScreen(vm: AppViewModel, onClose: () -> Unit) {
    val pager = rememberPagerState { 2 }
    var options by remember { mutableStateOf(false) }
    Column(Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding()) {
        Box(Modifier.fillMaxWidth().height(52.dp)) {
            Box(
                Modifier.align(Alignment.CenterStart).padding(start = 10.dp).size(44.dp).pressable(onClose)
                    .semantics { contentDescription = "收起播放器" },
                contentAlignment = Alignment.Center,
            ) {
                Icon(SilkIcons.Down, contentDescription = null, tint = LocalSilk.current.fg, modifier = Modifier.size(24.dp))
            }
            PageDots(pager.currentPage, Modifier.align(Alignment.Center))
        }
        HorizontalPager(pager, Modifier.weight(1f), beyondViewportPageCount = 1) { page ->
            if (page == 0) CoverPage(vm) else LyricsPage(vm) { options = true }
        }
    }
    if (options) LyricOptions(vm) { options = false }
}

@Composable
private fun PageDots(page: Int, modifier: Modifier) {
    val silk = LocalSilk.current
    Row(modifier, horizontalArrangement = Arrangement.spacedBy(7.dp), verticalAlignment = Alignment.CenterVertically) {
        repeat(2) { i ->
            val color by animateColorAsState(if (i == page) silk.fg else silk.fg24, label = "dot")
            val width by animateDpAsState(if (i == page) 8.dp else 7.dp, label = "dotSize")
            Box(Modifier.size(width).clip(CircleShape).background(color))
        }
    }
}

@Composable
private fun CoverPage(vm: AppViewModel) {
    val silk = LocalSilk.current
    val song = vm.current
    Column(
        Modifier.fillMaxSize().padding(horizontal = 30.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Spacer(Modifier.weight(.8f))
        val scale by androidx.compose.animation.core.animateFloatAsState(if (vm.isPlaying) 1f else .9f, label = "cover")
        Box(Modifier.fillMaxWidth().widthIn(max = 420.dp).aspectRatio(1f), contentAlignment = Alignment.Center) {
            Cover(
                song?.cover.orEmpty(),
                Modifier
                    .fillMaxSize(scale)
                    .shadow(28.dp * scale, RoundedCornerShape(30.dp), ambientColor = silk.a, spotColor = silk.a),
                RoundedCornerShape(30.dp),
            )
        }
        Spacer(Modifier.height(30.dp))
        Text(
            song?.title ?: "還沒有播放", color = silk.fg, fontSize = 21.sp, fontWeight = FontWeight.SemiBold,
            textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis,
        )
        Spacer(Modifier.height(4.dp))
        Text(song?.artist.orEmpty(), color = silk.fg70, fontSize = 14.sp, textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Spacer(Modifier.height(26.dp))
        ProgressBar(vm)
        Spacer(Modifier.height(22.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            val liked = song != null && song.mid in vm.liked
            PlainButton(if (liked) SilkIcons.HeartFilled else SilkIcons.Heart, if (liked) "取消收藏" else "收藏") { song?.let(vm::toggleLike) }
            RoundButton(SilkIcons.Prev, "上一首", vm::previous, size = 54.dp, iconSize = 20.dp)
            RoundButton(if (vm.isPlaying) SilkIcons.Pause else SilkIcons.Play, "播放或暫停", vm::toggle, size = 70.dp, iconSize = 26.dp)
            RoundButton(SilkIcons.Next, "下一首", vm::next, size = 54.dp, iconSize = 20.dp)
            val (modeIcon, modeLabel) = when (vm.mode) {
                PlayMode.Sequence -> SilkIcons.Repeat to "順序播放"
                PlayMode.RepeatOne -> SilkIcons.RepeatOne to "單曲循環"
                PlayMode.Shuffle -> SilkIcons.Shuffle to "隨機播放"
            }
            PlainButton(modeIcon, modeLabel) { vm.cycleMode() }
        }
        Spacer(Modifier.weight(1.2f))
    }
}

@Composable
private fun PlainButton(icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, onClick: () -> Unit) {
    Box(
        Modifier.size(44.dp).pressable(onClick).semantics { contentDescription = label },
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = null, tint = LocalSilk.current.fg70, modifier = Modifier.size(22.dp))
    }
}

/** A plain progress bar, like the sketch: the played part thicker and brighter. Drag or tap to seek. */
@Composable
private fun ProgressBar(vm: AppViewModel) {
    val silk = LocalSilk.current
    val position = rememberPosition(vm)
    var dragging by remember { mutableStateOf(false) }
    var dragRatio by remember { mutableFloatStateOf(0f) }
    val duration = vm.durationMs.coerceAtLeast(1)
    val thickness by animateDpAsState(if (dragging) 8.dp else 5.dp, label = "bar")

    Column(Modifier.fillMaxWidth()) {
        Canvas(
            Modifier
                .fillMaxWidth()
                .height(28.dp)
                .semantics { contentDescription = "播放進度" }
                .pointerInput(duration) {
                    detectTapGestures { offset -> vm.seekTo((offset.x / size.width * duration).toLong()) }
                }
                .pointerInput(duration) {
                    detectHorizontalDragGestures(
                        onDragStart = { offset ->
                            dragging = true
                            dragRatio = (offset.x / size.width).coerceIn(0f, 1f)
                        },
                        onDragEnd = {
                            vm.seekTo((dragRatio * duration).toLong())
                            dragging = false
                        },
                        onDragCancel = { dragging = false },
                        onHorizontalDrag = { change, amount ->
                            change.consume()
                            dragRatio = (dragRatio + amount / size.width).coerceIn(0f, 1f)
                        },
                    )
                },
        ) {
            val ratio = if (dragging) dragRatio else (position.value.toFloat() / duration).coerceIn(0f, 1f)
            val track = 3.dp.toPx()
            val fill = thickness.toPx()
            val y = size.height / 2
            drawRoundRect(silk.fg24, Offset(0f, y - track / 2), Size(size.width, track), CornerRadius(track / 2))
            val played = size.width * ratio
            if (played > 0) drawRoundRect(silk.fg, Offset(0f, y - fill / 2), Size(played.coerceAtLeast(fill), fill), CornerRadius(fill / 2))
        }
        Row(Modifier.fillMaxWidth()) {
            val shown = if (dragging) (dragRatio * duration).toLong() else position.value
            Text(formatTime(shown / 1000.0), color = silk.fg70, fontSize = 12.sp, modifier = Modifier.weight(1f))
            Text(formatTime(duration / 1000.0), color = silk.fg45, fontSize = 12.sp)
        }
    }
}

@Composable
private fun LyricsPage(vm: AppViewModel, onOptions: () -> Unit) {
    val silk = LocalSilk.current
    Box(Modifier.fillMaxSize()) {
        LyricsView(vm, Modifier.fillMaxSize().padding(bottom = 92.dp))
        RoundButton(
            SilkIcons.Sliders, "歌詞設定", onOptions, size = 56.dp, iconSize = 22.dp,
            modifier = Modifier.align(Alignment.BottomStart).padding(start = 22.dp, bottom = 22.dp),
        )
        RoundButton(
            if (vm.isPlaying) SilkIcons.Pause else SilkIcons.Play, "播放或暫停", vm::toggle, size = 56.dp, iconSize = 21.dp,
            modifier = Modifier.align(Alignment.BottomEnd).padding(end = 22.dp, bottom = 22.dp),
        )
        if (vm.current != null) {
            Text(
                vm.current?.title.orEmpty(), color = silk.fg45, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                textAlign = TextAlign.Center,
                modifier = Modifier.align(Alignment.BottomCenter).padding(start = 96.dp, end = 96.dp, bottom = 40.dp),
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun LyricOptions(vm: AppViewModel, onDismiss: () -> Unit) {
    val silk = LocalSilk.current
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = silk.sheet, contentColor = silk.fg) {
        Column(Modifier.padding(start = 24.dp, end = 24.dp, bottom = 36.dp)) {
            Text("歌詞設定", fontSize = 20.sp, fontWeight = FontWeight.SemiBold, color = silk.fg)
            Spacer(Modifier.height(18.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("字體大小", color = silk.fg, fontSize = 15.sp, modifier = Modifier.weight(1f))
                Text("${vm.lyricSize.toInt()}", color = silk.fg70, fontSize = 14.sp)
            }
            Slider(
                value = vm.lyricSize, onValueChange = { vm.changeLyricSize(it.toInt().toFloat()) }, valueRange = 18f..36f,
                colors = SliderDefaults.colors(thumbColor = silk.fg, activeTrackColor = silk.fg, inactiveTrackColor = silk.fg24),
            )
            Spacer(Modifier.height(10.dp))
            Text("對齊方式", color = silk.fg, fontSize = 15.sp)
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                listOf("靠左", "置中", "靠右").forEachIndexed { i, label -> Chip(label, vm.lyricAlign == i) { vm.changeLyricAlign(i) } }
            }
            Spacer(Modifier.height(18.dp))
            Text("效果", color = silk.fg, fontSize = 15.sp)
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Chip("遠處模糊", vm.lyricBlur) { vm.changeLyricBlur(!vm.lyricBlur) }
                Chip("目前行加粗", vm.lyricBold) { vm.changeLyricBold(!vm.lyricBold) }
            }
        }
    }
}

@Composable
fun Chip(label: String, selected: Boolean, onClick: () -> Unit) {
    val silk = LocalSilk.current
    Box(
        Modifier
            .pressable(onClick)
            .clip(RoundedCornerShape(22.dp))
            .background(if (selected) silk.fg24 else silk.fg07)
            .border(1.dp, if (selected) silk.fg45 else silk.fg12, RoundedCornerShape(22.dp))
            .padding(horizontal = 18.dp, vertical = 10.dp)
            .semantics { contentDescription = label + if (selected) "，已選取" else "" },
    ) {
        Text(label, color = silk.fg, fontSize = 14.sp)
    }
}

