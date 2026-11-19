package app.silk.ui

import android.graphics.Bitmap
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

/** Three colours taken from the cover: a deep base (a), a second tone (b) and a light sheen (c). */
@Immutable
data class CoverPalette(val a: Color, val b: Color, val c: Color, val light: Boolean) {
    companion object {
        val Default = CoverPalette(hsl(208f, .62f, .15f), hsl(190f, .62f, .28f), hsl(178f, .5f, .68f), false)

        /** The same rules as the Windows app, so both pick the same colours for a cover. */
        fun from(bitmap: Bitmap): CoverPalette {
            val small = Bitmap.createScaledBitmap(bitmap, 32, 32, true)
            val pixels = IntArray(32 * 32)
            small.getPixels(pixels, 0, 32, 0, 0, 32, 32)
            if (small != bitmap) small.recycle()

            val buckets = 24
            val weight = DoubleArray(buckets)
            val saturation = DoubleArray(buckets)
            var lightSum = 0.0
            var satSum = 0.0
            for (pixel in pixels) {
                val (h, s, l) = toHsl(((pixel shr 16) and 255) / 255.0, ((pixel shr 8) and 255) / 255.0, (pixel and 255) / 255.0)
                lightSum += l
                satSum += s
                val bucket = (h / 360 * buckets).toInt() % buckets
                val w = s * (1 - abs(2 * l - 1)) + .002
                weight[bucket] += w
                saturation[bucket] += s * w
            }
            val first = weight.indices.maxBy { weight[it] }
            var second = -1
            for (i in 0 until buckets) {
                val gap = abs(i - first)
                if (min(gap, buckets - gap) < 3) continue
                if (second < 0 || weight[i] > weight[second]) second = i
            }
            val hue = ((first + .5) * 360 / buckets).toFloat()
            val hasSecond = second >= 0 && weight[second] > weight[first] * .16
            val hue2 = if (hasSecond) ((second + .5) * 360 / buckets).toFloat() else hue + 26
            var sat = (saturation[first] / weight[first]).toFloat()
            val averageLight = lightSum / pixels.size
            if (satSum / pixels.size < .07) sat = .08f

            if (averageLight > .72) {
                return CoverPalette(hsl(hue, min(sat, .28f), .87f), hsl(hue2, min(sat, .2f), .79f), hsl(hue, min(sat, .34f), .97f), true)
            }
            val s1 = if (sat <= .08f) .08f else (sat * .8f).coerceIn(.3f, .56f)
            return CoverPalette(hsl(hue, s1, .15f), hsl(hue2, s1 * .88f, .26f), hsl(hue2, min(s1 + .08f, .62f), .66f), false)
        }

        private fun toHsl(r: Double, g: Double, b: Double): Triple<Double, Double, Double> {
            val maxC = max(r, max(g, b))
            val minC = min(r, min(g, b))
            val l = (maxC + minC) / 2
            val d = maxC - minC
            if (d < 1e-6) return Triple(0.0, 0.0, l)
            val s = d / (1 - abs(2 * l - 1))
            val h = when (maxC) {
                r -> (g - b) / d + (if (g < b) 6 else 0)
                g -> (b - r) / d + 2
                else -> (r - g) / d + 4
            }
            return Triple(h * 60, s, l)
        }

        fun hsl(h: Float, s: Float, l: Float): Color = Color.hsl(((h % 360) + 360) % 360, s.coerceIn(0f, 1f), l.coerceIn(0f, 1f))
    }
}

/** Live colours for the whole UI; every colour eases toward the current cover. */
@Immutable
data class SilkColors(
    val a: Color,
    val b: Color,
    val c: Color,
    val fg: Color,
    val light: Boolean,
) {
    val fg70 get() = fg.copy(alpha = .7f)
    val fg45 get() = fg.copy(alpha = .45f)
    val fg24 get() = fg.copy(alpha = .24f)
    val fg12 get() = fg.copy(alpha = .12f)
    val fg07 get() = fg.copy(alpha = .07f)

    /** The frosted panel behind the home page's content. */
    val panel get() = if (light) Color.White.copy(alpha = .38f) else lerp(a, Color.Black, .25f).copy(alpha = .42f)

    /** Sheets and menus: solid enough to read over moving silk. */
    val sheet get() = if (light) lerp(c, Color.White, .4f) else lerp(a, Color.Black, .35f)
}

val LocalSilk = staticCompositionLocalOf { SilkColors(CoverPalette.Default.a, CoverPalette.Default.b, CoverPalette.Default.c, Color.White, false) }

private val Ink = Color(38, 31, 26)

@Composable
fun SilkTheme(palette: CoverPalette, content: @Composable () -> Unit) {
    val spec = tween<Color>(900)
    val a by animateColorAsState(palette.a, spec, label = "a")
    val b by animateColorAsState(palette.b, spec, label = "b")
    val c by animateColorAsState(palette.c, spec, label = "c")
    val fg by animateColorAsState(if (palette.light) Ink else Color.White, spec, label = "fg")
    CompositionLocalProvider(LocalSilk provides SilkColors(a, b, c, fg, palette.light), content = content)
}
