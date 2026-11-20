package app.silk.ui

import androidx.compose.foundation.background
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
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** The play queue: tap to jump, arrows to move a song up or down, × to remove. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun QueueSheet(vm: AppViewModel, onDismiss: () -> Unit) {
    val silk = LocalSilk.current
    val list = rememberLazyListState()
    LaunchedEffect(Unit) { if (vm.queueIndex > 2) list.scrollToItem(vm.queueIndex - 2) }
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = silk.sheet, contentColor = silk.fg) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 22.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("播放清單", fontSize = 20.sp, fontWeight = FontWeight.SemiBold, color = silk.fg)
                Text("${vm.queue.size} 首", fontSize = 13.sp, color = silk.fg70)
            }
            if (vm.queue.size > 1) Chip("清空", false) { vm.clearQueue() }
        }
        Spacer(Modifier.height(8.dp))
        LazyColumn(state = list, modifier = Modifier.heightIn(max = 520.dp).edgeFade(list),contentPadding = PaddingValues(start = 12.dp, end = 12.dp, bottom = 28.dp)) {
            itemsIndexed(vm.queue, key = { i, song -> "$i:${song.mid}" }) { index, song ->
                val current = index == vm.queueIndex
                Row(
                    Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(14.dp))
                        .background(if (current) silk.fg12 else silk.fg07.copy(alpha = 0f))
                        .pressable { vm.skipTo(index) }
                        .padding(start = 8.dp, top = 6.dp, bottom = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Cover(song.cover, Modifier.size(42.dp), RoundedCornerShape(9.dp))
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(song.title, color = silk.fg, fontSize = 14.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                            fontWeight = if (current) FontWeight.SemiBold else FontWeight.Normal)
                        Text(song.artist, color = silk.fg70, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                    if (index > 0) QueueAction(SilkIcons.Back, "上移", rotate = true) { vm.moveInQueue(index, index - 1) }
                    if (!current) QueueAction(SilkIcons.Close, "移除") { vm.removeFromQueue(index) }
                }
            }
        }
    }
}

@Composable
private fun QueueAction(icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, rotate: Boolean = false, onClick: () -> Unit) {
    val silk = LocalSilk.current
    Box(Modifier.size(40.dp).pressable(onClick).semantics { contentDescription = label }, contentAlignment = Alignment.Center) {
        Icon(
            icon, contentDescription = null, tint = silk.fg70,
            modifier = Modifier.size(16.dp).then(if (rotate) Modifier.rotate(90f) else Modifier),
        )
    }
}

/** Account: the signed-in name, stream quality and sign-out. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AccountSheet(vm: AppViewModel, onDismiss: () -> Unit) {
    val silk = LocalSilk.current
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = silk.sheet, contentColor = silk.fg) {
        Column(Modifier.padding(start = 24.dp, end = 24.dp, bottom = 36.dp)) {
            Text(vm.profile?.name ?: "QQ 音樂", fontSize = 20.sp, fontWeight = FontWeight.SemiBold, color = silk.fg)
            Spacer(Modifier.height(20.dp))
            Text("音質", color = silk.fg, fontSize = 15.sp)
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                listOf("best" to "最佳", "320" to "320k", "128" to "128k").forEach { (value, label) ->
                    Chip(label, vm.quality == value) { vm.setStreamQuality(value) }
                }
            }
            Text("最佳音質需要帳號有對應權限；拿不到時會自動退回較低音質。", color = silk.fg45, fontSize = 12.sp,
                modifier = Modifier.padding(top = 8.dp))
            Spacer(Modifier.height(26.dp))
            Chip("登出", false) {
                onDismiss()
                vm.signOut()
            }
        }
    }
}
