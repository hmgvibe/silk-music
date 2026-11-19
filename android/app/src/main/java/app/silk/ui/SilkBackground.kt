package app.silk.ui

import android.graphics.RuntimeShader
import android.os.Build
import androidx.annotation.RequiresApi
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.ShaderBrush
import androidx.compose.ui.graphics.toArgb

/** The same flowing silk as the Windows app (an AGSL port of its pixel shader). Android 13+; a gradient before that. */
@Composable
fun SilkBackground(moving: Boolean, modifier: Modifier = Modifier) {
    val colors = LocalSilk.current
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
        ShaderSilk(moving, colors, modifier)
    } else {
        Box(modifier.fillMaxSize().background(Brush.linearGradient(listOf(colors.a, colors.b, colors.a))))
    }
}

@RequiresApi(Build.VERSION_CODES.TIRAMISU)
@Composable
private fun ShaderSilk(moving: Boolean, colors: SilkColors, modifier: Modifier) {
    val shader = remember { RuntimeShader(SHADER) }
    var time by remember { mutableFloatStateOf(8f) }
    val speed by rememberUpdatedState(if (moving) 1f else .35f)

    LaunchedEffect(Unit) {
        var last = 0L
        var sinceDraw = 0f
        while (true) {
            withFrameNanos { now ->
                val dt = if (last == 0L) 0f else ((now - last) / 1e9f).coerceAtMost(.1f)
                last = now
                sinceDraw += dt
                // The silk moves slowly, so 30 updates a second look the same as 60 and cost half.
                if (sinceDraw >= 1 / 30f) {
                    time += sinceDraw * speed * .62f
                    sinceDraw = 0f
                }
            }
        }
    }

    Box(
        modifier
            .fillMaxSize()
            .drawBehind {
                shader.setFloatUniform("size", size.width, size.height)
                shader.setFloatUniform("time", time)
                shader.setFloatUniform("glow", .35f)
                shader.setColorUniform("colorA", colors.a.toArgb())
                shader.setColorUniform("colorB", colors.b.toArgb())
                shader.setColorUniform("colorC", colors.c.toArgb())
                drawRect(ShaderBrush(shader))
            },
    )
}

private const val SHADER = """
uniform float2 size;
uniform float time;
uniform float glow;
layout(color) uniform half4 colorA;
layout(color) uniform half4 colorB;
layout(color) uniform half4 colorC;

half4 main(float2 coord) {
    float2 uv = coord / size;
    float t = time;
    float u = uv.x;
    float v = uv.y;
    float p = sin(u * 3.1 + t * .5 + 2. * sin(v * 2.3 - t * .33));
    float q = sin(v * 4. - t * .28 + 2.4 * sin(u * 2.6 + t * .2));
    float f = sin((p + q) * 2.3 + u * 1.6);
    float sheen = pow((f + 1.) * .5, 3.) * glow * .5;
    float crease = pow(abs(sin((p - q) * 2.6 + v * 1.8)), 14.) * glow * .07;
    half3 c = mix(colorA.rgb, colorB.rgb, half((p + 1.) * .36 * clamp(glow * 3., 0., 1.)));
    c = mix(c, colorC.rgb, half(clamp(sheen + crease, 0., 1.)));
    float n = fract(sin(dot(coord, float2(12.9898, 78.233))) * 43758.5453);
    return half4(c + half((n - .5) / 170.), 1.);
}
"""
