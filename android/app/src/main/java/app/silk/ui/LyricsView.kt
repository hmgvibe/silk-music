package app.silk.ui

import android.os.Build
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameMillis
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.BlurredEdgeTreatment
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.silk.data.LyricLine
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlin.math.sin

private const val LEAD = .15
private val SIDE = 28.dp

/** The playback position in milliseconds, refreshed every frame; reading it in a draw block costs no recomposition. */
@Composable
fun rememberPosition(vm: AppViewModel): State<Long> {
    val state = remember { mutableLongStateOf(vm.positionMs()) }
    LaunchedEffect(vm) {
        while (true) withFrameMillis { state.longValue = vm.positionMs() }
    }
    return state
}

/**
 * Lyrics that scroll line by line. Each line follows on its own spring, a beat after the one above it,
 * so a change of line ripples down the page. Far lines blur. Lines can be dragged to browse and tapped to seek.
 * There is no word-by-word timing: QQ Music's word timing is not used, and none is estimated.
 */
@Composable
fun LyricsView(vm: AppViewModel, modifier: Modifier = Modifier) {
    val silk = LocalSilk.current
    val lines = vm.lyrics
    if (lines.isEmpty()) {
        Box(modifier, contentAlignment = Alignment.Center) {
            Text(vm.lyricsNote ?: "", color = silk.fg70, fontSize = 15.sp)
        }
        return
    }

    val playback = rememberPosition(vm)
    // The lyric clock: playback position plus this song's lyric shift.
    val position = remember { derivedStateOf { playback.value + vm.lyricOffset } }
    val timed = lines.any { it.timed }
    val look = vm.look
    val align = look.lyricAlign
    val fontSize = look.lyricSize
    val activeScale = look.lyricScale / 100f
    val dimmed = look.lyricDim / 100f

    BoxWithConstraints(modifier.clipToBounds().fadeEdges({ 1f }, { 1f }, topSize = 56.dp, bottomSize = 72.dp)) {
        val density = LocalDensity.current
        val measurer = rememberTextMeasurer()
        val sidePx = with(density) { SIDE.toPx() }
        val textWidthPx = ((constraints.maxWidth - sidePx * 2) / activeScale).toInt().coerceAtLeast(1)
        val textWidth = with(density) { textWidthPx.toDp() }
        val viewHeight = constraints.maxHeight.toFloat()
        val lineStyle = TextStyle(fontSize = fontSize.sp, fontWeight = FontWeight.SemiBold, lineHeight = (fontSize * 1.28f).sp)
        val creditStyle = TextStyle(fontSize = (fontSize * .62f).sp, fontWeight = FontWeight.Normal, lineHeight = (fontSize * .9f).sp)
        // Line pitch, as on Windows, in multiples of the font size; the gap is what is left after the line itself.
        val gap = with(density) { (fontSize * (look.lyricSpacing / 10f - 1.28f).coerceAtLeast(.2f)).sp.toPx() }
        val dotBox = with(density) { (fontSize * 1.1f).sp.toPx() }

        val heights = remember(lines, textWidthPx, fontSize) {
            lines.map { line ->
                when {
                    line.isBreak -> dotBox
                    else -> measurer.measure(line.text, if (line.isCredit) creditStyle else lineStyle,
                        constraints = Constraints(maxWidth = textWidthPx)).size.height.toFloat()
                }
            }
        }
        val tops = remember(heights, gap) {
            val result = FloatArray(heights.size)
            var y = 0f
            heights.forEachIndexed { i, h ->
                result[i] = y
                y += h + if (lines[i].isCredit) gap * .55f else gap
            }
            result
        }

        val active by remember(lines) { derivedStateOf { if (timed) activeIndex(lines, position.value / 1000.0) else -1 } }
        val focus = active.coerceAtLeast(0)
        val anchor = viewHeight * .36f
        fun target(index: Int) = anchor - (tops[index] + heights[index] / 2)

        val offsets = remember(lines, heights) { List(lines.size) { Animatable(target(focus)) } }
        val manual = remember(lines) { Animatable(0f) }
        var browsing by remember { mutableStateOf(false) }
        var released by remember { mutableLongStateOf(0L) }
        val scope = rememberCoroutineScope()

        LaunchedEffect(focus, offsets) {
            val goal = target(focus)
            offsets.forEachIndexed { i, line ->
                launch {
                    // Lines above the new one move first; each line below starts a beat later.
                    delay((i - focus + 2).coerceIn(0, 9) * 42L)
                    line.animateTo(goal, spring(dampingRatio = .86f, stiffness = 140f))
                }
            }
        }

        // After browsing, the view drifts back to the sung line.
        LaunchedEffect(browsing, released) {
            if (browsing || !timed || manual.value == 0f) return@LaunchedEffect
            delay(2600)
            manual.animateTo(0f, spring(dampingRatio = .9f, stiffness = 90f))
        }

        Box(
            Modifier
                .fillMaxSize()
                .pointerInput(lines, heights) {
                    detectVerticalDragGestures(
                        onDragStart = { browsing = true },
                        onDragEnd = {
                            browsing = false
                            released = System.currentTimeMillis()
                        },
                        onDragCancel = {
                            browsing = false
                            released = System.currentTimeMillis()
                        },
                        onVerticalDrag = { change, amount ->
                            change.consume()
                            val base = offsets[focus].value
                            val lowest = target(lines.lastIndex) - base
                            val highest = target(0) - base
                            scope.launch { manual.snapTo((manual.value + amount).coerceIn(lowest, highest)) }
                        },
                    )
                },
        ) {
            lines.forEachIndexed { i, line ->
                key(i) {
                    LyricRow(
                        line = line,
                        top = tops[i],
                        offset = offsets[i],
                        manual = manual,
                        isActive = i == active,
                        distance = abs(i - focus),
                        timed = timed,
                        browsing = browsing,
                        blur = look.lyricBlur,
                        bold = look.lyricBold,
                        align = align,
                        activeScale = activeScale,
                        dimmed = dimmed,
                        textWidth = textWidth,
                        style = if (line.isCredit) creditStyle else lineStyle,
                        dotBox = dotBox,
                        position = position,
                        onSeek = {
                            scope.launch { manual.animateTo(0f, spring(dampingRatio = .9f, stiffness = 200f)) }
                            vm.seekTo(((line.time * 1000).toLong() - vm.lyricOffset).coerceAtLeast(0))
                        },
                    )
                }
            }
        }
    }
}

private fun activeIndex(lines: List<LyricLine>, seconds: Double): Int {
    var result = -1
    for (i in lines.indices) {
        val line = lines[i]
        if (line.time > seconds + LEAD) break
        if (!line.isCredit) result = i
    }
    return result
}

@Composable
private fun LyricRow(
    line: LyricLine,
    top: Float,
    offset: Animatable<Float, *>,
    manual: Animatable<Float, *>,
    isActive: Boolean,
    distance: Int,
    timed: Boolean,
    browsing: Boolean,
    blur: Boolean,
    bold: Boolean,
    align: Int,
    activeScale: Float,
    dimmed: Float,
    textWidth: androidx.compose.ui.unit.Dp,
    style: TextStyle,
    dotBox: Float,
    position: State<Long>,
    onSeek: () -> Unit,
) {
    val silk = LocalSilk.current
    val emphasis by animateFloatAsState(if (isActive) 1f else 0f, tween(420), label = "emphasis")
    val blurTarget = if (blur && timed && !browsing && !line.isCredit && distance > 1) ((distance - 1).coerceAtMost(4) * 1.1f).dp else 0.dp
    val blurRadius by animateDpAsState(blurTarget, tween(500), label = "blur")
    val pivot = when (align) {
        0 -> 0f
        2 -> 1f
        else -> .5f
    }
    val boxAlign = when (align) {
        0 -> Alignment.CenterStart
        2 -> Alignment.CenterEnd
        else -> Alignment.Center
    }
    val canSeek = timed && !line.isCredit && !line.isBreak

    Box(
        Modifier
            .fillMaxWidth()
            .offset { IntOffset(0, (top + offset.value + manual.value).roundToInt()) }
            .padding(horizontal = SIDE)
            .then(
                if (blurRadius > .2.dp && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) Modifier.blur(blurRadius, BlurredEdgeTreatment.Unbounded)
                else Modifier,
            ),
        contentAlignment = boxAlign,
    ) {
        if (line.isBreak) {
            BreakDots(line, position, emphasis, dotBox)
            return@Box
        }
        val dim = if (line.isCredit) dimmed * .84f else if (timed) dimmed else .9f
        Text(
            text = line.text,
            style = style.copy(fontWeight = if (line.isCredit) FontWeight.Normal else if (bold) FontWeight.SemiBold else FontWeight.Medium),
            color = silk.fg,
            textAlign = when (align) {
                0 -> TextAlign.Start
                2 -> TextAlign.End
                else -> TextAlign.Center
            },
            modifier = Modifier
                .width(textWidth)
                .graphicsLayer {
                    val scale = 1 + (activeScale - 1) * emphasis
                    scaleX = scale
                    scaleY = scale
                    alpha = dim + (1 - dim) * emphasis
                    transformOrigin = TransformOrigin(pivot, .5f)
                }
                .then(
                    if (canSeek) Modifier.clickable(remember { MutableInteractionSource() }, null, role = Role.Button, onClick = onSeek)
                    else Modifier,
                ),
        )
    }
}

/** Three dots that fill one after another across an instrumental break, breathing while they wait. */
@Composable
private fun BreakDots(line: LyricLine, position: State<Long>, appear: Float, box: Float) {
    val silk = LocalSilk.current
    val density = LocalDensity.current
    val radius = box * .19f
    val step = radius * 3.1f
    Canvas(Modifier.size(with(density) { (step * 2 + radius * 2).toDp() }, with(density) { box.toDp() })) {
        if (appear < .01f) return@Canvas
        val seconds = position.value / 1000.0
        val span = (line.end - line.time).coerceAtLeast(.1)
        val progress = ((seconds - line.time) / span).coerceIn(0.0, 1.0).toFloat()
        val leaving = ((line.end - seconds) / .45).coerceIn(0.0, 1.0).toFloat()
        val breath = 1 + .1f * sin(seconds * Math.PI * 2 / 2.4).toFloat()
        val scale = appear * (1 - (1 - leaving) * (1 - leaving) * (1 - leaving)) * breath
        for (i in 0 until 3) {
            val lit = (progress * 3 - i).coerceIn(0f, 1f)
            drawCircle(
                color = silk.fg.copy(alpha = .28f + .72f * lit),
                radius = radius * scale,
                center = Offset(radius + i * step, size.height / 2),
            )
        }
    }
}
