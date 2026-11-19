package app.silk.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.unit.dp

/** The Windows app's line icons, drawn from the same 24×24 path data. */
object SilkIcons {
    val Play = icon("M8 5.2 V18.8 L19 12 Z", filled = true)
    val Pause = icon("M8 5 V19 M16 5 V19", width = 2.4f)
    val Prev = icon("M18.5 6 V18 L9 12 Z M5.5 6 V18", filled = true)
    val Next = icon("M5.5 6 V18 L15 12 Z M18.5 6 V18", filled = true)
    val List = icon("M4 7 H20 M4 12 H20 M4 17 H13")
    val Search = icon("M11 4 A7 7 0 1 0 11 18 A7 7 0 1 0 11 4 Z M16.2 16.2 L20.5 20.5")
    val Disc = icon("M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 Z M12 9.5 A2.5 2.5 0 1 0 12 14.5 A2.5 2.5 0 1 0 12 9.5 Z")
    val Heart = icon("M12 20 C12 20 4.5 15.4 3 10.8 C2 7.4 4.2 5 7 5 C9 5 10.6 6 12 8 C13.4 6 15 5 17 5 C19.8 5 22 7.4 21 10.8 C19.5 15.4 12 20 12 20 Z")
    val HeartFilled = icon("M12 20 C12 20 4.5 15.4 3 10.8 C2 7.4 4.2 5 7 5 C9 5 10.6 6 12 8 C13.4 6 15 5 17 5 C19.8 5 22 7.4 21 10.8 C19.5 15.4 12 20 12 20 Z", filled = true)
    val User = icon("M12 4 A4 4 0 1 0 12 12 A4 4 0 1 0 12 4 Z M4.5 20 C5.5 16.5 8.4 15 12 15 C15.6 15 18.5 16.5 19.5 20")
    val Down = icon("M6 9 L12 15 L18 9")
    val Back = icon("M15 5.5 L8.5 12 L15 18.5")
    val Forward = icon("M9 5.5 L15.5 12 L9 18.5")
    val Sliders = icon("M4 7 H13 M17 7 H20 M13 5 H17 V9 H13 Z M4 17 H7 M11 17 H20 M7 15 H11 V19 H7 Z")
    val Repeat = icon("M17 3 L21 7 L17 11 M3 11 V9.5 A2.5 2.5 0 0 1 5.5 7 H21 M7 21 L3 17 L7 13 M21 13 V14.5 A2.5 2.5 0 0 1 18.5 17 H3")
    val RepeatOne = icon("M17 3 L21 7 L17 11 M3 11 V9.5 A2.5 2.5 0 0 1 5.5 7 H21 M7 21 L3 17 L7 13 M21 13 V14.5 A2.5 2.5 0 0 1 18.5 17 H3 M11 11 L12.5 10 V14")
    val Shuffle = icon("M16 3 H21 V8 M3 20 L21 3 M21 16 V21 H16 M15 15 L21 21 M3 4 L9 10")
    val Close = icon("M6 6 L18 18 M18 6 L6 18")
    val Grip = icon("M9 6 H9.01 M15 6 H15.01 M9 12 H9.01 M15 12 H15.01 M9 18 H9.01 M15 18 H15.01", width = 2.6f)
    val More = icon("M5 12 H5.01 M12 12 H12.01 M19 12 H19.01", width = 2.8f)

    private fun icon(data: String, filled: Boolean = false, width: Float = 1.8f): ImageVector =
        ImageVector.Builder(defaultWidth = 24.dp, defaultHeight = 24.dp, viewportWidth = 24f, viewportHeight = 24f)
            .addPath(
                pathData = PathParser().parsePathString(data).toNodes(),
                fill = if (filled) SolidColor(Color.White) else null,
                stroke = SolidColor(Color.White),
                strokeLineWidth = width,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            )
            .build()
}
