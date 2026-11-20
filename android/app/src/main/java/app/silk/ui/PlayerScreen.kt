package app.silk.ui

import android.content.res.Configuration
import kotlin.math.roundToInt
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.displayCutout
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.ui.platform.LocalConfiguration
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
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
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

/**
 * Upright: two pages, as in the sketch — the cover with its controls, then the lyrics, with dots showing which is open.
 * Sideways (the button at the top right): cover and controls on the left, lyrics on the right.
 */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun PlayerScreen(vm: AppViewModel, onClose: () -> Unit) {
    var options by remember { mutableStateOf(false) }
    val sideways = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    if (sideways) {
        LandscapePlayer(vm, onClose) { options = true }
    } else {
        val pager = rememberPagerState { 2 }
        Column(Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding()) {
            Box(Modifier.fillMaxWidth().height(52.dp)) {
                TopButton(SilkIcons.Down, "收起播放器", onClose, Modifier.align(Alignment.CenterStart).padding(start = 10.dp))
                PageDots(pager.currentPage, Modifier.align(Alignment.Center))
                TopButton(SilkIcons.Landscape, "橫屏", { vm.landscape = true }, Modifier.align(Alignment.CenterEnd).padding(end = 10.dp))
            }
            HorizontalPager(pager, Modifier.weight(1f), beyondViewportPageCount = 1) { page ->
                if (page == 0) CoverPage(vm) else LyricsPage(vm) { options = true }
            }
        }
    }
    if (options) LyricOptions(vm) { options = false }
}

@Composable
private fun TopButton(icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, onClick: () -> Unit, modifier: Modifier = Modifier) {
    Box(
        modifier.size(44.dp).pressable(onClick).semantics { contentDescription = label },
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = null, tint = LocalSilk.current.fg, modifier = Modifier.size(24.dp))
    }
}

/** Sideways: the cover, title and controls fill the left, the lyrics the right; the buttons sit in the top corners. */
@Composable
private fun LandscapePlayer(vm: AppViewModel, onClose: () -> Unit, onOptions: () -> Unit) {
    val silk = LocalSilk.current
    val song = vm.current
    Box(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.displayCutout)) {
        Row(Modifier.fillMaxSize()) {
            Column(
                Modifier.weight(.44f).fillMaxHeight().padding(start = 60.dp, end = 16.dp, top = 20.dp, bottom = 14.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                PlayerCover(vm, Modifier.weight(1f, fill = false).aspectRatio(1f))
                Spacer(Modifier.height(14.dp))
                Text(
                    song?.title ?: "還沒有播放", color = silk.fg, fontSize = 18.sp, fontWeight = FontWeight.SemiBold,
                    textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis,
                )
                if (song != null) {
                    ArtistLink(song, vm::openSinger) {
                        Text(song.artist, color = silk.fg70, fontSize = 13.sp, textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
                Spacer(Modifier.height(6.dp))
                ProgressBar(vm)
                Spacer(Modifier.height(4.dp))
                Controls(vm, compact = true)
            }
            LyricsView(vm, Modifier.weight(.56f).fillMaxHeight().padding(top = 52.dp, bottom = 10.dp, end = 6.dp))
        }
        TopButton(SilkIcons.Down, "收起播放器", onClose, Modifier.align(Alignment.TopStart).padding(start = 8.dp, top = 6.dp))
        Row(Modifier.align(Alignment.TopEnd).padding(end = 8.dp, top = 6.dp)) {
            TopButton(SilkIcons.Sliders, "歌詞設定", onOptions)
            TopButton(SilkIcons.Portrait, "豎屏", { vm.landscape = false })
        }
    }
}

/** The cover, a little smaller while paused. */
@Composable
private fun PlayerCover(vm: AppViewModel, modifier: Modifier) {
    val silk = LocalSilk.current
    // Small, standard and large, as on Windows; a little smaller again while paused.
    val size = when (vm.look.coverSize) {
        0 -> .74f
        2 -> 1f
        else -> .94f
    }
    val scale by androidx.compose.animation.core.animateFloatAsState((if (vm.isPlaying) 1f else .9f) * size, label = "cover")
    Box(modifier, contentAlignment = Alignment.Center) {
        Cover(
            vm.current?.cover.orEmpty(),
            Modifier
                .fillMaxSize(scale)
                .shadow(28.dp * scale, RoundedCornerShape(30.dp), ambientColor = silk.a, spotColor = silk.a),
            RoundedCornerShape(30.dp),
        )
    }
}

@Composable
private fun Controls(vm: AppViewModel, compact: Boolean = false) {
    val song = vm.current
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
        val liked = song != null && song.mid in vm.liked
        PlainButton(if (liked) SilkIcons.HeartFilled else SilkIcons.Heart, if (liked) "取消收藏" else "收藏") { song?.let(vm::toggleLike) }
        val side = if (compact) 46.dp else 54.dp
        val middle = if (compact) 58.dp else 70.dp
        RoundButton(SilkIcons.Prev, "上一首", vm::previous, size = side, iconSize = side * .37f)
        RoundButton(if (vm.isPlaying) SilkIcons.Pause else SilkIcons.Play, "播放或暫停", vm::toggle, size = middle, iconSize = middle * .37f)
        RoundButton(SilkIcons.Next, "下一首", vm::next, size = side, iconSize = side * .37f)
        val (modeIcon, modeLabel) = when (vm.mode) {
            PlayMode.Sequence -> SilkIcons.Repeat to "清單循環"
            PlayMode.RepeatOne -> SilkIcons.RepeatOne to "單曲循環"
            PlayMode.Shuffle -> SilkIcons.Shuffle to "隨機播放"
        }
        PlainButton(modeIcon, modeLabel) { vm.cycleMode() }
    }
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
        Modifier.fillMaxSize().padding(horizontal = 18.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Spacer(Modifier.weight(.8f))
        PlayerCover(vm, Modifier.fillMaxWidth().widthIn(max = 460.dp).aspectRatio(1f))
        Spacer(Modifier.height(30.dp))
        Text(
            song?.title ?: "還沒有播放", color = silk.fg, fontSize = 21.sp, fontWeight = FontWeight.SemiBold,
            textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis,
        )
        Spacer(Modifier.height(1.dp))
        if (song != null) {
            ArtistLink(song, vm::openSinger) {
                Text(song.artist, color = silk.fg70, fontSize = 14.sp, textAlign = TextAlign.Center, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        } else {
            Spacer(Modifier.height(25.dp))
        }
        Spacer(Modifier.height(23.dp))
        Box(Modifier.padding(horizontal = 12.dp)) { ProgressBar(vm) }
        Spacer(Modifier.height(22.dp))
        Box(Modifier.padding(horizontal = 12.dp)) { Controls(vm) }
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
    ModalBottomSheet(
        onDismissRequest = onDismiss, containerColor = silk.sheet, contentColor = silk.fg,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
    ) {
        // Sideways the sheet is taller than the screen, so it scrolls.
        val scroll = rememberScrollState()
        Column(Modifier.edgeFade(scroll).verticalScroll(scroll).padding(start = 24.dp, end = 24.dp, bottom = 36.dp)) {
            Text("歌詞設定", fontSize = 20.sp, fontWeight = FontWeight.SemiBold, color = silk.fg)
            Text(
                if (vm.landscape) "橫屏的設定，和豎屏分開保存" else "豎屏的設定，和橫屏分開保存",
                color = silk.fg45, fontSize = 12.sp, modifier = Modifier.padding(top = 2.dp),
            )
            Spacer(Modifier.height(16.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("字體大小", color = silk.fg, fontSize = 15.sp, modifier = Modifier.weight(1f))
                Text("${vm.look.lyricSize.toInt()}", color = silk.fg70, fontSize = 14.sp)
            }
            Slider(
                value = vm.look.lyricSize, onValueChange = { vm.look.lyricSize = it.toInt().toFloat() }, valueRange = 18f..36f,
                colors = SliderDefaults.colors(thumbColor = silk.fg, activeTrackColor = silk.fg, inactiveTrackColor = silk.fg24),
            )
            Spacer(Modifier.height(10.dp))
            Text("對齊方式", color = silk.fg, fontSize = 15.sp)
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                listOf("靠左", "置中", "靠右").forEachIndexed { i, label -> Chip(label, vm.look.lyricAlign == i) { vm.look.lyricAlign = i } }
            }
            Spacer(Modifier.height(18.dp))
            Text("效果", color = silk.fg, fontSize = 15.sp)
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Chip("遠處模糊", vm.look.lyricBlur) { vm.look.lyricBlur = !vm.look.lyricBlur }
                Chip("目前行加粗", vm.look.lyricBold) { vm.look.lyricBold = !vm.look.lyricBold }
            }
            Spacer(Modifier.height(18.dp))
            LyricShift(vm)
            Spacer(Modifier.height(8.dp))
            Amount("目前行放大", "${vm.look.lyricScale}%")
            Slider(
                value = vm.look.lyricScale.toFloat(), onValueChange = { vm.look.lyricScale = (it / 2).roundToInt() * 2 }, valueRange = 100f..170f,
                colors = sliderColors(),
            )
            Amount("行距", "%.1f 倍".format(vm.look.lyricSpacing / 10f))
            Slider(
                value = vm.look.lyricSpacing.toFloat(), onValueChange = { vm.look.lyricSpacing = it.roundToInt() }, valueRange = 16f..40f,
                colors = sliderColors(),
            )
            Amount("其他行亮度", "${vm.look.lyricDim}%")
            Slider(
                value = vm.look.lyricDim.toFloat(), onValueChange = { vm.look.lyricDim = it.roundToInt() }, valueRange = 15f..100f,
                colors = sliderColors(),
            )
            Spacer(Modifier.height(6.dp))
            Text("封面大小", color = silk.fg, fontSize = 15.sp)
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                listOf("小", "標準", "大").forEachIndexed { i, label -> Chip(label, vm.look.coverSize == i) { vm.look.coverSize = i } }
            }
            Spacer(Modifier.height(22.dp))
            Chip("恢復預設", false) { vm.look.reset() }
        }
    }
}

/** Nudges lyrics earlier or later, in tenths of a second, saved for the output device (Bluetooth adds delay). */
@Composable
private fun LyricShift(vm: AppViewModel) {
    val silk = LocalSilk.current
    val offset = vm.lyricOffset
    LaunchedEffect(Unit) { vm.refreshOutput() }
    Row(verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text("歌詞微調", color = silk.fg, fontSize = 15.sp)
            Text("跟著輸出裝置記住 · 目前：${vm.outputDevice.label}", color = silk.fg45, fontSize = 12.sp,
                maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        if (offset != 0) {
            Text(
                "重設", color = silk.fg70, fontSize = 13.sp,
                modifier = Modifier.clip(RoundedCornerShape(10.dp)).pressable { vm.resetLyricShift() }.padding(8.dp, 4.dp),
            )
        }
    }
    Spacer(Modifier.height(10.dp))
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        StepButton("−0.5", "歌詞延後 0.5 秒") { vm.shiftLyrics(-500) }
        StepButton("−0.1", "歌詞延後 0.1 秒") { vm.shiftLyrics(-100) }
        val label = when {
            offset > 0 -> "提前 %.1f 秒".format(offset / 1000f)
            offset < 0 -> "延後 %.1f 秒".format(-offset / 1000f)
            else -> "未調整"
        }
        Text(label, color = if (offset == 0) silk.fg45 else silk.fg, fontSize = 14.sp, textAlign = TextAlign.Center,
            maxLines = 1, modifier = Modifier.weight(1f))
        StepButton("+0.1", "歌詞提前 0.1 秒") { vm.shiftLyrics(100) }
        StepButton("+0.5", "歌詞提前 0.5 秒") { vm.shiftLyrics(500) }
    }
}

@Composable
private fun Amount(label: String, value: String) {
    val silk = LocalSilk.current
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 10.dp)) {
        Text(label, color = silk.fg, fontSize = 15.sp, modifier = Modifier.weight(1f))
        Text(value, color = silk.fg70, fontSize = 14.sp)
    }
}

@Composable
private fun sliderColors() = LocalSilk.current.let { silk ->
    SliderDefaults.colors(thumbColor = silk.fg, activeTrackColor = silk.fg, inactiveTrackColor = silk.fg24)
}

@Composable
private fun StepButton(text: String, label: String, onClick: () -> Unit) {
    val silk = LocalSilk.current
    Box(
        Modifier
            .pressable(onClick)
            .clip(RoundedCornerShape(20.dp))
            .background(silk.fg07)
            .border(1.dp, silk.fg12, RoundedCornerShape(20.dp))
            .padding(horizontal = 12.dp, vertical = 9.dp)
            .semantics { contentDescription = label },
    ) {
        Text(text, color = silk.fg, fontSize = 14.sp)
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

