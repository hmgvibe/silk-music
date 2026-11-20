package app.silk.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.silk.data.Collection
import app.silk.data.Song

/** The home page from the sketch: a header row, one frosted card holding everything, and the mini player below. */
@Composable
fun HomeScreen(vm: AppViewModel, onAccount: () -> Unit) {
    val silk = LocalSilk.current
    Column(Modifier.fillMaxSize()) {
        Row(
            Modifier.fillMaxWidth().padding(start = 18.dp, end = 14.dp, top = 10.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                Modifier.size(48.dp).pressable(onAccount).clip(CircleShape).background(silk.fg12),
                contentAlignment = Alignment.Center,
            ) {
                val avatar = vm.profile?.avatarUrl.orEmpty()
                if (avatar.isNotEmpty()) Cover(avatar, Modifier.size(48.dp), CircleShape)
                else Icon(SilkIcons.User, contentDescription = "帳號", tint = silk.fg, modifier = Modifier.size(22.dp))
            }
            Spacer(Modifier.width(10.dp))
            Column(Modifier.weight(1f)) {
                Text(
                    vm.profile?.name ?: "未登入", color = silk.fg, fontSize = 17.sp, fontWeight = FontWeight.SemiBold,
                    maxLines = 1, overflow = TextOverflow.Ellipsis,
                )
                Text(if (vm.signedIn) greeting() else "點頭像登入", color = silk.fg70, fontSize = 12.sp, maxLines = 1)
            }
            // Four buttons at the top right, as on the Windows app's side bar: search, library, liked, settings.
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                SquareButton(SilkIcons.Search, "搜尋", { vm.open(Screen.Search) }, size = 44.dp)
                SquareButton(SilkIcons.Disc, "音樂庫", { vm.open(Screen.Library) }, size = 44.dp)
                SquareButton(SilkIcons.Heart, "我喜歡", { vm.openCollection(AppViewModel.LIKED) }, size = 44.dp)
                SquareButton(SilkIcons.Gear, "設定", { vm.open(Screen.Settings) }, size = 44.dp)
            }
        }

        Box(
            Modifier
                .weight(1f)
                .fillMaxWidth()
                .padding(horizontal = 14.dp)
                .clip(RoundedCornerShape(30.dp))
                .background(silk.panel)
                .border(1.dp, silk.fg07, RoundedCornerShape(30.dp)),
        ) {
            HomeContent(vm)
        }
    }
}

@Composable
private fun HomeContent(vm: AppViewModel) {
    val home = vm.home
    LazyColumn(contentPadding = PaddingValues(16.dp, 16.dp, 16.dp, 24.dp)) {
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                FeatureTile("猜你喜歡", "隨你口味播放", home.guessCover, Modifier.weight(1f)) { vm.openCollection(AppViewModel.GUESS) }
                FeatureTile("每日 30 首", "每天更新", home.dailyCover, Modifier.weight(1f)) { vm.openCollection(AppViewModel.DAILY) }
            }
        }

        if (vm.showRecommended && home.recommended.isNotEmpty()) {
            item { SectionTitle("為你推薦", "播放全部") { vm.play(home.recommended, 0) } }
            val rows = home.recommended.take(6).chunked(2)
            items(rows.size) { row ->
                Row(Modifier.padding(bottom = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    rows[row].forEach { song ->
                        val index = home.recommended.indexOf(song)
                        CompactSong(song, vm.current?.mid == song.mid, Modifier.weight(1f)) { vm.play(home.recommended, index) }
                    }
                    if (rows[row].size == 1) Spacer(Modifier.weight(1f))
                }
            }
        }

        if (vm.showPlaylists && home.playlists.isNotEmpty()) {
            item { SectionTitle("推薦歌單") }
            item { Shelf(home.playlists, vm) }
        }
        if (vm.showAlbums && home.albums.isNotEmpty()) {
            item { SectionTitle("新碟上架") }
            item { Shelf(home.albums, vm) }
        }

        if (home.loading && home.recommended.isEmpty()) item { Message("正在載入") }
        home.error?.let { item { Message(it) } }
    }
}

private fun greeting(): String {
    val hour = java.util.Calendar.getInstance().get(java.util.Calendar.HOUR_OF_DAY)
    return when (hour) {
        in 5..10 -> "早安"
        in 11..13 -> "午安"
        in 14..17 -> "下午好"
        else -> "晚上好"
    }
}

@Composable
private fun FeatureTile(title: String, subtitle: String, cover: String, modifier: Modifier, onClick: () -> Unit) {
    val silk = LocalSilk.current
    Box(modifier.aspectRatio(1f).pressable(onClick).clip(RoundedCornerShape(24.dp)).background(silk.fg12)) {
        if (cover.isNotEmpty()) Cover(cover, Modifier.fillMaxSize(), RoundedCornerShape(24.dp))
        Box(
            Modifier.fillMaxSize().background(
                Brush.verticalGradient(listOf(Color.Transparent, Color.Black.copy(alpha = .55f))),
            ),
        )
        Column(Modifier.align(Alignment.BottomStart).padding(14.dp)) {
            Text(title, color = Color.White, fontSize = 19.sp, fontWeight = FontWeight.Bold)
            Text(subtitle, color = Color.White.copy(alpha = .78f), fontSize = 12.sp)
        }
    }
}

@Composable
private fun CompactSong(song: Song, isCurrent: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val silk = LocalSilk.current
    Row(
        modifier
            .clip(RoundedCornerShape(14.dp))
            .background(if (isCurrent) silk.fg12 else silk.fg07)
            .pressable(onClick)
            .padding(8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Cover(song.cover, Modifier.size(40.dp), RoundedCornerShape(9.dp))
        Spacer(Modifier.width(9.dp))
        Column(Modifier.weight(1f)) {
            Text(song.title, color = silk.fg, fontSize = 13.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                fontWeight = if (isCurrent) FontWeight.SemiBold else FontWeight.Normal)
            Text(song.artist, color = silk.fg70, fontSize = 11.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun Shelf(items: List<Collection>, vm: AppViewModel) {
    LazyRow(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        items(items.size) { i -> CollectionCard(items[i], Modifier.width(112.dp)) { vm.openCollection(items[i]) } }
    }
}

@Composable
fun CollectionCard(item: Collection, modifier: Modifier = Modifier, round: Boolean = false, onClick: () -> Unit) {
    val silk = LocalSilk.current
    Column(modifier.pressable(onClick)) {
        Cover(item.cover, Modifier.fillMaxWidth().aspectRatio(1f), if (round) CircleShape else RoundedCornerShape(20.dp))
        Spacer(Modifier.height(7.dp))
        Text(item.title, color = silk.fg, fontSize = 13.sp, maxLines = 2, overflow = TextOverflow.Ellipsis, lineHeight = 17.sp)
        Text(item.subtitle, color = silk.fg45, fontSize = 11.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}
