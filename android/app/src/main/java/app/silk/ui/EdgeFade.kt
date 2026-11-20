package app.silk.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.gestures.ScrollableState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.BlendMode
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.CompositingStrategy
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/** Fades a scrolling list out toward its top and bottom edges; each edge fades only while there is more to scroll that way. */
@Composable
fun Modifier.edgeFade(state: ScrollableState, size: Dp = 28.dp): Modifier {
    val top by animateFloatAsState(if (state.canScrollBackward) 1f else 0f, tween(220), label = "fadeTop")
    val bottom by animateFloatAsState(if (state.canScrollForward) 1f else 0f, tween(220), label = "fadeBottom")
    return fadeEdges({ top }, { bottom }, size, size)
}

/** Fades content out over [top] and [bottom] at each edge; the lambdas give how strong each fade is, 0 to 1. */
fun Modifier.fadeEdges(top: () -> Float, bottom: () -> Float, topSize: Dp, bottomSize: Dp): Modifier = this
    .graphicsLayer { compositingStrategy = CompositingStrategy.Offscreen }
    .drawWithContent {
        drawContent()
        val topStrength = top()
        if (topStrength > 0f) {
            val h = topSize.toPx().coerceAtMost(size.height / 2)
            drawRect(
                Brush.verticalGradient(listOf(Color.Black.copy(alpha = 1f - topStrength), Color.Black), startY = 0f, endY = h),
                topLeft = Offset.Zero, size = Size(size.width, h), blendMode = BlendMode.DstIn,
            )
        }
        val bottomStrength = bottom()
        if (bottomStrength > 0f) {
            val h = bottomSize.toPx().coerceAtMost(size.height / 2)
            drawRect(
                Brush.verticalGradient(listOf(Color.Black, Color.Black.copy(alpha = 1f - bottomStrength)), startY = size.height - h, endY = size.height),
                topLeft = Offset(0f, size.height - h), size = Size(size.width, h), blendMode = BlendMode.DstIn,
            )
        }
    }
