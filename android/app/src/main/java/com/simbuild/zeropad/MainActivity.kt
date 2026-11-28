package com.simbuild.zeropad

import android.content.Context
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlin.math.roundToInt

private val Paper = Color(0xFFF7F7F5)
private val Ink = Color(0xFF111111)
private val Muted = Color(0xFF777777)
private val Border = Color(0xFFD9D9D5)
private val DarkBg = Color(0xFF0B0D10)
private val DarkSurface = Color(0xFF15181D)
private val DarkInk = Color(0xFFF4F6F8)
private val DarkMuted = Color(0xFFA1A8B2)
private val DarkBorder = Color(0xFF303640)
private val Accent = Color(0xFF4C8DFF)

private const val SEND_TICK_MS = 16L      // ~60 snapshots per second while something changes
private const val KEEPALIVE_MS = 100L     // 10 per second while idle (the PC releases the pad after 1 s of silence)

/** Remembers the PC address and key between launches. Stored privately in the app's own storage. */
private class SavedSettings(context: Context) {
    private val prefs = context.getSharedPreferences("zeropad", Context.MODE_PRIVATE)
    var host: String
        get() = prefs.getString("host", "") ?: ""
        set(v) { prefs.edit().putString("host", v).apply() }
    var key: String
        get() = prefs.getString("key", "") ?: ""
        set(v) { prefs.edit().putString("key", v).apply() }
    var usb: Boolean
        get() = prefs.getBoolean("usb", false)
        set(v) { prefs.edit().putBoolean("usb", v).apply() }
    var dark: Boolean
        get() = prefs.getBoolean("dark", true)
        set(v) { prefs.edit().putBoolean("dark", v).apply() }
}

class MainActivity : ComponentActivity() {
    /** True while the app is in the background: every button and stick is released then. */
    private var inBackground by mutableStateOf(false)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        window.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        val settings = SavedSettings(this)
        setContent {
            var darkMode by remember { mutableStateOf(settings.dark) }
            val bg = if (darkMode) DarkBg else Paper
            val surface = if (darkMode) DarkSurface else Color.White
            val ink = if (darkMode) DarkInk else Ink
            val muted = if (darkMode) DarkMuted else Muted
            val border = if (darkMode) DarkBorder else Border
            MaterialTheme(
                colorScheme = if (darkMode) darkColorScheme(primary = Accent, background = bg, surface = surface, onSurface = ink)
                else lightColorScheme(primary = Color(0xFF245FC7), background = bg, surface = surface, onSurface = ink)
            ) {
                Surface(Modifier.fillMaxSize(), color = bg) {
                    ControllerScreen(
                        settings = settings,
                        inBackground = inBackground,
                        darkMode = darkMode,
                        setDarkMode = { darkMode = it; settings.dark = it },
                        bg = bg, surface = surface, ink = ink, muted = muted, border = border
                    )
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        inBackground = false
        WindowInsetsControllerCompat(window, window.decorView).apply {
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            hide(WindowInsetsCompat.Type.systemBars())
        }
    }

    override fun onPause() {
        inBackground = true
        super.onPause()
    }
}

@Composable
private fun ControllerScreen(
    settings: SavedSettings,
    inBackground: Boolean,
    darkMode: Boolean,
    setDarkMode: (Boolean) -> Unit,
    bg: Color, surface: Color, ink: Color, muted: Color, border: Color
) {
    val pressed = remember { mutableStateListOf<String>() }
    val client = remember { GamepadTransportClient() }
    var transport by remember { mutableStateOf(if (settings.usb) TransportMode.USB else TransportMode.WIFI) }
    var host by remember { mutableStateOf(settings.host) }
    var pairingKey by remember { mutableStateOf(settings.key) }
    var connected by remember { mutableStateOf(false) }
    var transportReady by remember { mutableStateOf(false) }
    var hostSeen by remember { mutableStateOf(false) }
    var latencyMs by remember { mutableStateOf<Long?>(null) }
    var status by remember { mutableStateOf("Not connected") }
    var leftX by remember { mutableFloatStateOf(0f) }
    var leftY by remember { mutableFloatStateOf(0f) }
    var rightX by remember { mutableFloatStateOf(0f) }
    var rightY by remember { mutableFloatStateOf(0f) }

    fun releaseEverything() {
        pressed.clear()
        leftX = 0f; leftY = 0f; rightX = 0f; rightY = 0f
    }

    DisposableEffect(client) { onDispose { client.close() } }
    LaunchedEffect(inBackground) { if (inBackground) releaseEverything() }

    LaunchedEffect(connected) {
        if (!connected) return@LaunchedEffect
        val startedAt = System.currentTimeMillis()

        // Sender: reacts to a change within one tick, and keeps the link alive when nothing changes.
        launch {
            var lastSent: GamepadSnapshot? = null
            var lastSentAt = 0L
            while (isActive) {
                val snapshot = GamepadSnapshot(
                    buttons = pressed.filter { it != "LT" && it != "RT" }.toSet(),
                    leftX = leftX, leftY = leftY, rightX = rightX, rightY = rightY,
                    leftTrigger = if ("LT" in pressed) 1f else 0f,
                    rightTrigger = if ("RT" in pressed) 1f else 0f
                )
                val now = System.currentTimeMillis()
                if (!transportReady) {
                    try {
                        withContext(Dispatchers.IO) { client.connect(host, pairingKey, transport) }
                        transportReady = true
                        status = "Connecting · waiting for the PC to answer"
                    } catch (cancel: CancellationException) {
                        throw cancel
                    } catch (error: Exception) {
                        status = if (transport == TransportMode.USB) "USB not ready: run the adb reverse command, then wait"
                        else "Can't start: ${error.message ?: "check the IP and key"}"
                        delay(700)
                        continue
                    }
                }
                if (snapshot != lastSent || now - lastSentAt >= KEEPALIVE_MS) {
                    try {
                        withContext(Dispatchers.IO) { client.send(snapshot) }
                        lastSent = snapshot
                        lastSentAt = now
                    } catch (cancel: CancellationException) {
                        throw cancel
                    } catch (error: Exception) {
                        transportReady = false
                        hostSeen = false
                        latencyMs = null
                        status = "Connection interrupted · reconnecting"
                        withContext(Dispatchers.IO) { client.close() }
                    }
                }
                delay(SEND_TICK_MS)
            }
        }

        // Receiver: acknowledgements tell us the PC is there, and how fast.
        launch {
            while (isActive) {
                if (!transportReady) { delay(100); continue }
                val ack = withContext(Dispatchers.IO) { client.receiveAck() }
                if (ack.received) {
                    hostSeen = true
                    latencyMs = ack.roundTripMs
                    status = if (ack.virtualOutput) "Connected · Xbox controller active"
                    else "Connected, but the PC has no virtual controller (install ViGEmBus)"
                } else {
                    if (hostSeen) {
                        hostSeen = false
                        latencyMs = null
                        status = "PC stopped answering"
                    } else if (System.currentTimeMillis() - startedAt > 3000) {
                        status = if (transport == TransportMode.USB) "No answer: is the companion running and adb reverse active?"
                        else "No answer from the PC: same Wi-Fi? correct IP and key? firewall on Private networks?"
                    }
                    delay(100)
                }
            }
        }
    }

    if (!connected) {
        Column(
            Modifier.fillMaxSize().safeDrawingPadding().verticalScroll(rememberScrollState()).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("ZEROPAD", fontSize = 26.sp, color = ink)
                    Text("PHONE AS YOUR GAMEPAD", fontSize = 11.sp, color = muted, letterSpacing = 1.4.sp)
                }
                Button(onClick = { setDarkMode(!darkMode) }, shape = RoundedCornerShape(12.dp)) { Text(if (darkMode) "Light" else "Dark") }
            }
            Column(
                Modifier.fillMaxWidth().background(surface, RoundedCornerShape(20.dp)).border(1.dp, border, RoundedCornerShape(20.dp)).padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Text("Connect to PC", fontSize = 20.sp, color = ink)
                Text("Enter the details shown in the ZeroPad window on your PC. They are remembered.", fontSize = 12.sp, color = muted)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    for (mode in TransportMode.values()) {
                        val on = transport == mode
                        Button(
                            onClick = { transport = mode },
                            modifier = Modifier.weight(1f),
                            colors = ButtonDefaults.buttonColors(containerColor = if (on) Accent else surface, contentColor = if (on) Color.White else ink)
                        ) { Text(if (mode == TransportMode.WIFI) "Wi-Fi" else "USB / ADB") }
                    }
                }
                if (transport == TransportMode.WIFI) {
                    OutlinedTextField(host, { host = it }, Modifier.fillMaxWidth(), singleLine = true, label = { Text("PC address") }, placeholder = { Text("192.168.1.10") })
                } else {
                    Text("Enable USB debugging, plug in the cable, then on the PC run:  adb reverse tcp:26761 tcp:26761", fontSize = 11.sp, color = muted)
                }
                OutlinedTextField(pairingKey, { pairingKey = it }, Modifier.fillMaxWidth(), singleLine = true, label = { Text("Pairing key") })
                Button(
                    onClick = {
                        settings.host = host.trim(); settings.key = pairingKey.trim(); settings.usb = transport == TransportMode.USB
                        host = host.trim(); pairingKey = pairingKey.trim()
                        status = "Connecting…"
                        transportReady = false; hostSeen = false
                        connected = true
                    },
                    enabled = pairingKey.isNotBlank() && (transport == TransportMode.USB || host.isNotBlank()),
                    modifier = Modifier.fillMaxWidth().height(50.dp),
                    shape = RoundedCornerShape(14.dp)
                ) { Text("Open controller") }
                Text(status, fontSize = 12.sp, color = muted)
            }
            Text("QUICK SETUP", fontSize = 11.sp, color = muted, letterSpacing = 1.4.sp)
            for (step in listOf(
                "01  Install ViGEmBus on the PC (once), then run ZeroPad.exe",
                "02  Same private Wi-Fi as the PC, or connect USB/ADB",
                "03  Type the PC address and pairing key shown in the ZeroPad window"
            )) Text(step, color = ink, modifier = Modifier.fillMaxWidth().background(surface, RoundedCornerShape(12.dp)).padding(14.dp))
        }
    } else {
        BoxWithConstraints(Modifier.fillMaxSize().safeDrawingPadding().padding(horizontal = 14.dp, vertical = 8.dp)) {
            val topBar = 40.dp
            val shoulderRow = 46.dp
            val gaps = 28.dp
            val byHeight = maxHeight - topBar - shoulderRow - gaps
            val byWidth = (maxWidth - 150.dp) / 4   // two sticks + two 3x3 button blocks share the width
            val stick = minOf(byHeight, byWidth, 190.dp).coerceAtLeast(96.dp)
            val cell = stick / 3

            Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Row(Modifier.fillMaxWidth().height(topBar), verticalAlignment = Alignment.CenterVertically) {
                    Text("ZEROPAD", fontSize = 16.sp, color = ink)
                    Spacer(Modifier.width(12.dp))
                    Text(
                        (if (hostSeen) "● PC connected" else "● Waiting for PC") + (latencyMs?.let { " · $it ms" } ?: ""),
                        fontSize = 12.sp, color = if (hostSeen) Accent else muted
                    )
                    Spacer(Modifier.weight(1f))
                    Text(status, fontSize = 10.sp, color = muted, maxLines = 1, modifier = Modifier.weight(3f))
                    OutlinedButton(onClick = { connected = false; transportReady = false; hostSeen = false; latencyMs = null; status = "Disconnected"; client.close(); releaseEverything() }, modifier = Modifier.height(34.dp)) { Text("Exit", fontSize = 12.sp) }
                }
                Row(Modifier.fillMaxWidth().height(shoulderRow), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        ShoulderButton("LT", "LT" in pressed, ink, surface, border) { update(pressed, "LT", it) }
                        ShoulderButton("LB", "LB" in pressed, ink, surface, border) { update(pressed, "LB", it) }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        ShoulderButton("SELECT", "SELECT" in pressed, ink, surface, border) { update(pressed, "SELECT", it) }
                        ShoulderButton("START", "START" in pressed, ink, surface, border) { update(pressed, "START", it) }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        ShoulderButton("RB", "RB" in pressed, ink, surface, border) { update(pressed, "RB", it) }
                        ShoulderButton("RT", "RT" in pressed, ink, surface, border) { update(pressed, "RT", it) }
                    }
                }
                Row(Modifier.fillMaxWidth().weight(1f), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                    StickPad(stick, "L3" in pressed, bg, ink, border, Accent) { s -> leftX = s.x; leftY = s.y }
                    DiamondButtons(cell, listOf("UP", "LEFT", "RIGHT", "DOWN"), listOf("▲", "◀", "▶", "▼"), pressed, ink, surface, border)
                    Spacer(Modifier.weight(1f))
                    DiamondButtons(cell, listOf("Y", "X", "B", "A"), listOf("Y", "X", "B", "A"), pressed, ink, surface, border)
                    StickPad(stick, "R3" in pressed, bg, ink, border, Accent) { s -> rightX = s.x; rightY = s.y }
                }
            }
        }
    }
}

private fun update(pressed: MutableList<String>, key: String, down: Boolean) {
    if (down && key !in pressed) pressed.add(key)
    if (!down) pressed.remove(key)
}

/**
 * A thumb stick. The finger can touch anywhere in the circle (or start there and drift outside);
 * the knob follows it and the value comes from [StickMath], which is covered by unit tests.
 */
@Composable
private fun StickPad(size: Dp, active: Boolean, bg: Color, ink: Color, border: Color, accent: Color, onStick: (Stick) -> Unit) {
    var thumb by remember { mutableStateOf<Offset?>(null) }   // finger position relative to the centre, in px
    val density = LocalDensity.current
    val knob = size * 0.4f
    val knobPx = with(density) { knob.toPx() }
    val sizePx = with(density) { size.toPx() }
    val radiusPx = (sizePx - knobPx) / 2f                       // distance of the knob centre at full deflection
    val shown = thumb?.let { StickMath.knobOffset(it.x, it.y, radiusPx) } ?: Pair(0f, 0f)

    Box(
        Modifier.size(size)
            .background(bg, CircleShape)
            .border(if (thumb != null) 2.dp else 1.dp, if (thumb != null) accent else border, CircleShape)
            .pointerInput(radiusPx) {
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false)
                    down.consume()
                    val centre = Offset(this.size.width / 2f, this.size.height / 2f)
                    fun move(position: Offset) {
                        val d = position - centre
                        thumb = d
                        onStick(StickMath.fromTouch(d.x, d.y, radiusPx))
                    }
                    move(down.position)
                    var tracking = true
                    while (tracking) {
                        val event = awaitPointerEvent()
                        val change = event.changes.firstOrNull { it.id == down.id }
                        if (change == null || !change.pressed) {
                            tracking = false
                        } else {
                            change.consume()
                            move(change.position)
                        }
                    }
                    thumb = null
                    onStick(Stick.CENTRE)
                }
            },
        contentAlignment = Alignment.Center
    ) {
        Box(
            Modifier.offset { IntOffset(shown.first.roundToInt(), shown.second.roundToInt()) }
                .size(knob)
                .background(if (thumb != null || active) accent else ink, CircleShape)
        )
    }
}

/** Four buttons laid out as a diamond (top, left, right, bottom), used for the D-pad and for A/B/X/Y. */
@Composable
private fun DiamondButtons(cell: Dp, names: List<String>, labels: List<String>, pressed: MutableList<String>, ink: Color, surface: Color, border: Color) {
    Box(Modifier.size(cell * 3)) {
        val spots = listOf(Pair(1, 0), Pair(0, 1), Pair(2, 1), Pair(1, 2))
        for (i in names.indices) {
            Box(Modifier.offset(x = cell * spots[i].first, y = cell * spots[i].second)) {
                PadButton(labels[i], names[i] in pressed, cell, ink, surface, border) { update(pressed, names[i], it) }
            }
        }
    }
}

/** Presses while a finger is down on it and releases when the finger lifts or slides off. */
private fun Modifier.pressable(onInput: (Boolean) -> Unit): Modifier = pointerInput(Unit) {
    awaitEachGesture {
        val down = awaitFirstDown(requireUnconsumed = false)
        down.consume()
        onInput(true)
        try {
            waitForUpOrCancellation()
        } finally {
            onInput(false)
        }
    }
}

@Composable
private fun PadButton(label: String, active: Boolean, cell: Dp, ink: Color, surface: Color, border: Color, onInput: (Boolean) -> Unit) {
    val haptic = LocalHapticFeedback.current
    Box(
        Modifier.size(cell).padding(2.dp)
            .background(if (active) Accent else surface, CircleShape)
            .border(1.dp, if (active) Accent else border, CircleShape)
            .pressable { down -> if (down) haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove); onInput(down) },
        contentAlignment = Alignment.Center
    ) {
        Text(label, fontSize = 15.sp, color = if (active) Color.White else ink)
    }
}

@Composable
private fun ShoulderButton(label: String, active: Boolean, ink: Color, surface: Color, border: Color, onInput: (Boolean) -> Unit) {
    val haptic = LocalHapticFeedback.current
    Box(
        Modifier.height(40.dp).width(if (label.length > 2) 76.dp else 60.dp)
            .background(if (active) Accent else surface, RoundedCornerShape(12.dp))
            .border(1.dp, if (active) Accent else border, RoundedCornerShape(12.dp))
            .pressable { down -> if (down) haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove); onInput(down) },
        contentAlignment = Alignment.Center
    ) {
        Text(label, fontSize = if (label.length > 2) 11.sp else 14.sp, color = if (active) Color.White else ink)
    }
}
