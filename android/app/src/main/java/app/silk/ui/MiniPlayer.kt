package app.silk.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** The floating pill at the bottom (cover, title, previous / play / next) and the round queue button beside it. */
@Composable
fun MiniPlayer(vm: AppViewModel, onOpen: () -> Unit, onQueue: () -> Unit, modifier: Modifier = Modifier) {
    val silk = LocalSilk.current
    val song = vm.current
    Row(modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        Row(
            Modifier
                .weight(1f)
                .height(64.dp)
                .clip(RoundedCornerShape(32.dp))
                .background(silk.sheet.copy(alpha = .9f))
                .border(1.dp, silk.fg12, RoundedCornerShape(32.dp))
                .pressable(onOpen)
                .semantics { contentDescription = "打開播放器" }
                .padding(start = 8.dp, end = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Cover(song?.cover.orEmpty(), Modifier.size(48.dp), CircleShape)
            Spacer(Modifier.width(10.dp))
            Column(Modifier.weight(1f)) {
                Text(song?.title ?: "還沒有播放", color = silk.fg, fontSize = 14.sp, fontWeight = FontWeight.SemiBold,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                if (song != null) Text(song.artist, color = silk.fg70, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            SmallControl(SilkIcons.Prev, "上一首") { vm.previous() }
            SmallControl(if (vm.isPlaying) SilkIcons.Pause else SilkIcons.Play, "播放或暫停", big = true) { vm.toggle() }
            SmallControl(SilkIcons.Next, "下一首") { vm.next() }
        }
        Spacer(Modifier.width(10.dp))
        RoundButton(SilkIcons.List, "播放清單", onQueue, size = 64.dp, iconSize = 24.dp,
            modifier = Modifier.clip(CircleShape).background(silk.sheet.copy(alpha = .9f)))
    }
}

@Composable
private fun SmallControl(icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, big: Boolean = false, onClick: () -> Unit) {
    val silk = LocalSilk.current
    val size = if (big) 42.dp else 36.dp
    Box(
        Modifier
            .padding(horizontal = 2.dp)
            .size(size)
            .pressable(onClick)
            .clip(CircleShape)
            .border(1.2.dp, silk.fg24, CircleShape)
            .semantics { contentDescription = label },
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = null, tint = silk.fg, modifier = Modifier.size(if (big) 17.dp else 14.dp))
    }
}
