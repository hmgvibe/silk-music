package app.silk.ui

import android.Manifest
import android.content.Intent
import android.content.pm.ActivityInfo
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.silk.SilkApp
import kotlinx.coroutines.delay

class MainActivity : ComponentActivity() {
    private val vm: AppViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        // For checks without an account: adb shell am start -n app.silk/.ui.MainActivity --ez demo true
        if (intent.hasExtra(EXTRA_DEMO)) {
            val settings = (application as SilkApp).settings
            val demo = intent.getBooleanExtra(EXTRA_DEMO, false)
            if (settings.demo != demo) settings.demo = demo
        }
        enableEdgeToEdge(SystemBarStyle.dark(android.graphics.Color.TRANSPARENT), SystemBarStyle.dark(android.graphics.Color.TRANSPARENT))
        super.onCreate(savedInstanceState)
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
        }
        if (intent.getBooleanExtra(EXTRA_OPEN_PLAYER, false)) vm.playerOpen = true
        setContent { SilkRoot(vm) }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        if (intent.getBooleanExtra(EXTRA_OPEN_PLAYER, false)) vm.playerOpen = true
    }

    companion object {
        const val EXTRA_OPEN_PLAYER = "openPlayer"
        const val EXTRA_DEMO = "demo"
    }
}

@Composable
private fun SilkRoot(vm: AppViewModel) {
    SilkTheme(vm.themePalette, solidPanel = vm.panelMode == 1) {
        val silk = LocalSilk.current
        var queueOpen by remember { mutableStateOf(false) }
        var accountOpen by remember { mutableStateOf(false) }
        BackHandler(enabled = vm.playerOpen || vm.screens.size > 1) { vm.back() }
        PlayerOrientation(vm)

        Box(Modifier.fillMaxSize().background(silk.a)) {
            SilkBackground(moving = vm.isPlaying, glow = glowFor(vm.glow))

            if (!vm.ready) {
                Starting(vm)
            } else {
                Column(Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding()) {
                    AnimatedContent(
                        targetState = vm.screens.last(),
                        transitionSpec = {
                            val forward = vm.screens.contains(initialState) && targetState != initialState && vm.screens.last() == targetState
                            if (forward) {
                                (slideInHorizontally(tween(320)) { it / 4 } + fadeIn(tween(240))) togetherWith fadeOut(tween(160))
                            } else {
                                fadeIn(tween(240)) togetherWith (slideOutHorizontally(tween(320)) { it / 4 } + fadeOut(tween(200)))
                            }
                        },
                        modifier = Modifier.weight(1f),
                        label = "page",
                    ) { screen ->
                        when (screen) {
                            Screen.Home -> HomeScreen(vm, onAccount = { if (vm.signedIn) accountOpen = true else vm.open(Screen.Login) })
                            Screen.Search -> SearchScreen(vm)
                            Screen.Library -> LibraryScreen(vm)
                            Screen.Login -> LoginScreen(vm)
                            Screen.Settings -> SettingsScreen(vm)
                            is Screen.Songs -> SongsScreen(vm, screen.collection)
                        }
                    }
                    MiniPlayer(vm, onOpen = { if (vm.current != null) vm.playerOpen = true }, onQueue = { queueOpen = true })
                }
            }

            AnimatedVisibility(
                visible = vm.playerOpen,
                enter = slideInVertically(tween(380)) { it } + fadeIn(tween(200)),
                exit = slideOutVertically(tween(320)) { it } + fadeOut(tween(260)),
            ) {
                Box(Modifier.fillMaxSize()) {
                    SilkBackground(moving = vm.isPlaying, glow = glowFor(vm.glow))
                    PlayerScreen(vm, onClose = { vm.playerOpen = false })
                }
            }

            Toasts(vm, Modifier.align(Alignment.BottomCenter))
        }

        if (queueOpen) QueueSheet(vm) { queueOpen = false }
        if (accountOpen) AccountSheet(vm) { accountOpen = false }
        vm.playlistPick?.let { song -> PlaylistPickerSheet(vm, song) }
        vm.playlistNaming?.let { naming -> PlaylistNameDialog(vm, naming) }
    }
}

/**
 * The app stays upright; only the player turns sideways, when asked. Sideways, the system bars hide
 * (a swipe from the edge brings them back) so the lyrics get the whole height.
 */
@Composable
private fun PlayerOrientation(vm: AppViewModel) {
    val activity = LocalContext.current as? ComponentActivity ?: return
    val sideways = vm.playerOpen && vm.landscape
    // Dark status bar icons over light colours (a light cover or the fixed light theme).
    val light = vm.themePalette.light
    LaunchedEffect(light) {
        WindowCompat.getInsetsController(activity.window, activity.window.decorView).apply {
            isAppearanceLightStatusBars = light
            isAppearanceLightNavigationBars = light
        }
    }
    LaunchedEffect(vm.playerOpen) { if (!vm.playerOpen) vm.landscape = false }
    LaunchedEffect(sideways) {
        activity.requestedOrientation =
            if (sideways) ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE else ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
        val bars = WindowCompat.getInsetsController(activity.window, activity.window.decorView)
        if (sideways) {
            bars.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            bars.hide(WindowInsetsCompat.Type.systemBars())
        } else {
            bars.show(WindowInsetsCompat.Type.systemBars())
        }
    }
}

@Composable
private fun Starting(vm: AppViewModel) {
    val silk = LocalSilk.current
    Column(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally) {
        Spacer(Modifier.weight(1f))
        Text("絲音", color = silk.fg, fontSize = 30.sp)
        Spacer(Modifier.height(14.dp))
        val error = vm.startError
        Text(error ?: "正在啟動音樂服務", color = silk.fg70, fontSize = 14.sp)
        if (error != null) {
            Spacer(Modifier.height(18.dp))
            Chip("再試一次", false) { vm.retryStart() }
        }
        Spacer(Modifier.weight(1.3f))
    }
}

@Composable
private fun Toasts(vm: AppViewModel, modifier: Modifier) {
    val silk = LocalSilk.current
    var message by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(vm) {
        vm.toasts.collect {
            message = it
            delay(2200)
            message = null
        }
    }
    AnimatedVisibility(
        visible = message != null,
        enter = fadeIn() + slideInVertically { it / 2 },
        exit = fadeOut(),
        modifier = modifier.navigationBarsPadding().padding(bottom = 100.dp),
    ) {
        Text(
            message.orEmpty(), color = silk.fg, fontSize = 14.sp,
            modifier = Modifier.clip(RoundedCornerShape(20.dp)).background(silk.sheet).padding(horizontal = 18.dp, vertical = 11.dp),
        )
    }
}
