package app.silk.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * The Windows app's settings page: appearance, playback and which home sections show.
 * Its waveform options and "when closing the window" have no counterpart on the phone.
 */
@Composable
fun SettingsScreen(vm: AppViewModel) {
    val silk = LocalSilk.current
    val scroll = rememberScrollState()
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 10.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(44.dp).pressable { vm.back() }.semantics { contentDescription = "返回" }, contentAlignment = Alignment.Center) {
                Icon(SilkIcons.Back, contentDescription = null, tint = silk.fg, modifier = Modifier.size(24.dp))
            }
            Text("設定", color = silk.fg, fontSize = 19.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 4.dp))
        }
        Column(
            Modifier
                .weight(1f)
                .edgeFade(scroll)
                .verticalScroll(scroll)
                .padding(start = 22.dp, end = 22.dp, bottom = 28.dp),
        ) {
            Group("外觀", first = true)
            Choice("絲綢光效", listOf("關閉", "柔和", "適中", "明顯"), vm.glow) { vm.glow = it }
            Choice("視窗顏色", listOf("跟隨封面", "固定深色", "固定淺色"), vm.colorMode) { vm.colorMode = it }
            Choice("面板質感", listOf("通透", "沉穩"), vm.panelMode) { vm.panelMode = it }

            Group("播放")
            val qualities = listOf("best", "320", "128")
            Choice("音質", listOf("最高可用", "320K", "128K"), qualities.indexOf(vm.quality).coerceAtLeast(0)) {
                vm.setStreamQuality(qualities[it])
            }
            Note("最高音質需要帳號有對應權限；拿不到時會自動退回較低音質。")
            Choice("播放順序", listOf("清單循環", "單曲循環", "隨機播放"), vm.mode.ordinal) { vm.changeMode(PlayMode.entries[it]) }

            Group("首頁顯示")
            Chips {
                Chip("為你推薦", vm.showRecommended) { vm.showRecommended = !vm.showRecommended }
                Chip("推薦歌單", vm.showPlaylists) { vm.showPlaylists = !vm.showPlaylists }
                Chip("新碟上架", vm.showAlbums) { vm.showAlbums = !vm.showAlbums }
            }
        }
    }
}

@Composable
private fun Group(title: String, first: Boolean = false) {
    Text(
        title, color = LocalSilk.current.fg, fontSize = 17.sp, fontWeight = FontWeight.SemiBold,
        modifier = Modifier.padding(top = if (first) 6.dp else 26.dp, bottom = 4.dp),
    )
}

@Composable
private fun Choice(label: String, options: List<String>, selected: Int, onSelect: (Int) -> Unit) {
    Text(label, color = LocalSilk.current.fg70, fontSize = 14.sp, modifier = Modifier.padding(top = 12.dp, bottom = 8.dp))
    Chips {
        options.forEachIndexed { i, option -> Chip(option, selected == i) { onSelect(i) } }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Chips(content: @Composable () -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) { content() }
}

@Composable
private fun Note(text: String) {
    Spacer(Modifier.height(6.dp))
    Text(text, color = LocalSilk.current.fg45, fontSize = 12.sp)
}
