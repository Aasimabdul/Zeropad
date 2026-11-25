package com.simbuild.zeropad

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.collect
import androidx.compose.runtime.snapshotFlow
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

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            var darkMode by remember { mutableStateOf(false) }
            val bg = if (darkMode) DarkBg else Paper
            val surface = if (darkMode) DarkSurface else Color.White
            val ink = if (darkMode) DarkInk else Ink
            val muted = if (darkMode) DarkMuted else Muted
            val border = if (darkMode) DarkBorder else Border
            MaterialTheme(colorScheme = if (darkMode) darkColorScheme(primary = Color(0xFF4C8DFF), background = bg, surface = surface, onSurface = ink) else lightColorScheme(primary = Color(0xFF245FC7), background = bg, surface = surface, onSurface = ink)) {
                Surface(Modifier.fillMaxSize(), color = bg) { ControllerScreen(darkMode, { darkMode = it }, bg, surface, ink, muted, border) }
            }
        }
    }
}

@Composable
private fun ControllerScreen(darkMode: Boolean, setDarkMode: (Boolean) -> Unit, bg: Color, surface: Color, ink: Color, muted: Color, border: Color) {
    val pressed = remember { mutableStateListOf<String>() }
    val client = remember { GamepadTransportClient() }
    val scope = rememberCoroutineScope()
    var transport by remember { mutableStateOf(TransportMode.WIFI) }
    var host by remember { mutableStateOf("") }
    var pairingKey by remember { mutableStateOf("") }
    var connected by remember { mutableStateOf(false) }
    var transportReady by remember { mutableStateOf(false) }
    var hostSeen by remember { mutableStateOf(false) }
    var latencyMs by remember { mutableStateOf<Long?>(null) }
    var status by remember { mutableStateOf("Not connected") }
    var leftX by remember { mutableFloatStateOf(0f) }
    var leftY by remember { mutableFloatStateOf(0f) }
    var rightX by remember { mutableFloatStateOf(0f) }
    var rightY by remember { mutableFloatStateOf(0f) }

    DisposableEffect(client) {
        onDispose { client.close() }
    }

    LaunchedEffect(connected) {
        if (!connected) return@LaunchedEffect
        var latestSnapshot: GamepadSnapshot? = null

        launch {
            snapshotFlow {
                GamepadSnapshot(
                    buttons = pressed.filter { it != "LT" && it != "RT" }.toSet(),
                    leftX = leftX,
                    leftY = leftY,
                    rightX = rightX,
                    rightY = rightY,
                    leftTrigger = if ("LT" in pressed) 1f else 0f,
                    rightTrigger = if ("RT" in pressed) 1f else 0f
                )
            }.collect { latestSnapshot = it }
        }

        launch {
            while (isActive) {
                val snapshot = latestSnapshot
                if (snapshot != null) {
                    if (!transportReady) {
                        try {
                            withContext(Dispatchers.IO) { client.connect(host, pairingKey, transport) }
                            transportReady = true
                            status = "Transport ready · waiting for host"
                        } catch (cancel: CancellationException) {
                            throw cancel
                        } catch (error: Exception) {
                            status = "Reconnect failed: ${error.message ?: "check cable/network"}"
                            delay(500)
                            continue
                        }
                    }
                    try {
                        withContext(Dispatchers.IO) { client.send(snapshot) }
                    } catch (cancel: CancellationException) {
                        throw cancel
                    } catch (error: Exception) {
                        transportReady = false
                        hostSeen = false
                        latencyMs = null
                        status = "Transport interrupted · reconnecting"
                        withContext(Dispatchers.IO) { client.close() }
                    }
                }
                delay(if (transportReady) 50 else 500)
            }
        }

        launch {
            while (isActive) {
                if (!transportReady) {
                    delay(100)
                    continue
                }
                val ack = withContext(Dispatchers.IO) { client.receiveAck() }
                if (ack.received) {
                    hostSeen = true
                    latencyMs = ack.roundTripMs
                    status = if (ack.virtualOutput) "Connected · Xbox controller active" else "Connected · virtual driver unavailable"
                } else {
                    if (hostSeen) {
                        hostSeen = false
                        latencyMs = null
                        status = "Host not responding"
                    }
                    delay(100)
                }
            }
        }
    }

    if (!connected) {
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) { Text("ZEROPAD", fontSize = 26.sp, color = ink); Text("PHONE AS YOUR GAMEPAD", fontSize = 11.sp, color = muted, letterSpacing = 1.4.sp) }
                Button(onClick = { darkMode = !darkMode }, shape = RoundedCornerShape(12.dp)) { Text(if (darkMode) "☀ Light" else "☾ Dark") }
            }
            Column(Modifier.fillMaxWidth().background(surface, RoundedCornerShape(20.dp)).border(1.dp, border, RoundedCornerShape(20.dp)).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text("Connect to PC", fontSize = 20.sp, color = ink)
                Text("Enter the details from the Windows companion.", fontSize = 12.sp, color = muted)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onClick = { if (!connected) transport = TransportMode.WIFI }, enabled = !connected, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = if (transport == TransportMode.WIFI) ink else surface, contentColor = if (transport == TransportMode.WIFI) surface else ink)) { Text("Wi-Fi") }
                    Button(onClick = { if (!connected) transport = TransportMode.USB }, enabled = !connected, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = if (transport == TransportMode.USB) ink else surface, contentColor = if (transport == TransportMode.USB) surface else ink)) { Text("USB / ADB") }
                }
                if (transport == TransportMode.WIFI) OutlinedTextField(host, { host = it }, Modifier.fillMaxWidth(), singleLine = true, label = { Text("Windows PC IP address") }, placeholder = { Text("192.168.1.10") })
                else Text("Enable USB debugging and run: adb reverse tcp:26761 tcp:26761", fontSize = 11.sp, color = muted)
                OutlinedTextField(pairingKey, { pairingKey = it }, Modifier.fillMaxWidth(), singleLine = true, label = { Text("Pairing key (Base64)") })
                Button(onClick = { connected = true; status = "Connecting to Windows…" }, enabled = pairingKey.isNotBlank() && (transport == TransportMode.USB || host.isNotBlank()), modifier = Modifier.fillMaxWidth().height(50.dp), shape = RoundedCornerShape(14.dp)) { Text("Open controller  →") }
                Text(status, fontSize = 12.sp, color = muted)
            }
            Text("QUICK SETUP", fontSize = 11.sp, color = muted, letterSpacing = 1.4.sp)
            Text("01  Run ZeroPad Companion on Windows", color = ink, modifier = Modifier.fillMaxWidth().background(surface, RoundedCornerShape(12.dp)).padding(14.dp))
            Text("02  Use the same private Wi-Fi, or connect USB/ADB", color = ink, modifier = Modifier.fillMaxWidth().background(surface, RoundedCornerShape(12.dp)).padding(14.dp))
            Text("03  Copy the PC IP and pairing key into this screen", color = ink, modifier = Modifier.fillMaxWidth().background(surface, RoundedCornerShape(12.dp)).padding(14.dp))
        }
    } else {
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 14.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) { Text("ZEROPAD", fontSize = 23.sp, color = ink); Text(if (hostSeen) "● PC CONNECTED" else "● WAITING FOR PC", fontSize = 10.sp, color = muted) }
                Button(onClick = { darkMode = !darkMode }) { Text(if (darkMode) "☀" else "☾") }
                OutlinedButton(onClick = { connected = false; transportReady = false; hostSeen = false; latencyMs = null; status = "Disconnected"; client.close(); pressed.clear(); leftX = 0f; leftY = 0f; rightX = 0f; rightY = 0f }) { Text("Exit") }
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Column(Modifier.weight(1f).background(surface, RoundedCornerShape(12.dp)).border(1.dp, border, RoundedCornerShape(12.dp)).padding(12.dp)) { Text("LINK", fontSize = 10.sp, color = muted); Text(if (hostSeen) "Connected" else "Waiting", color = ink) }
                Column(Modifier.weight(1f).background(surface, RoundedCornerShape(12.dp)).border(1.dp, border, RoundedCornerShape(12.dp)).padding(12.dp)) { Text("ROUND TRIP", fontSize = 10.sp, color = muted); Text(latencyMs?.toString()?.plus(" ms") ?: "—", color = ink) }
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                StickPad("LEFT STICK", leftX, leftY, Modifier.weight(1f), bg, ink, border) { x, y -> leftX = x; leftY = y }
                StickPad("RIGHT STICK", rightX, rightY, Modifier.weight(1f), bg, ink, border) { x, y -> rightX = x; rightY = y }
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) { PadButton("↑", pressed.contains("UP"), ink, surface, border) { update(pressed, "UP", it) }; Row { PadButton("←", pressed.contains("LEFT"), ink, surface, border) { update(pressed, "LEFT", it) }; PadButton("↓", pressed.contains("DOWN"), ink, surface, border) { update(pressed, "DOWN", it) }; PadButton("→", pressed.contains("RIGHT"), ink, surface, border) { update(pressed, "RIGHT", it) } } }
                Column(horizontalAlignment = Alignment.CenterHorizontally) { PadButton("Y", pressed.contains("Y"), ink, surface, border) { update(pressed, "Y", it) }; Row { PadButton("X", pressed.contains("X"), ink, surface, border) { update(pressed, "X", it) }; Spacer(Modifier.width(12.dp)); PadButton("B", pressed.contains("B"), ink, surface, border) { update(pressed, "B", it) } }; PadButton("A", pressed.contains("A"), ink, surface, border) { update(pressed, "A", it) } }
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp)) { listOf("LB", "LT", "SELECT", "START", "RT", "RB").forEach { key -> PadButton(key, pressed.contains(key), ink, surface, border, compact = true) { update(pressed, key, it) } } }
            Column(Modifier.fillMaxWidth().background(surface, RoundedCornerShape(14.dp)).border(1.dp, border, RoundedCornerShape(14.dp)).padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("Input diagnostics", color = ink, fontSize = 14.sp)
                Text("Buttons: " + pressed.joinToString().ifEmpty { "none" }, color = muted, fontSize = 12.sp)
                Text("L: " + axisText(leftX) + ", " + axisText(leftY) + "    R: " + axisText(rightX) + ", " + axisText(rightY), color = muted, fontSize = 12.sp)
                Text(status, color = muted, fontSize = 12.sp)
                Text("20 Hz snapshots · RTT is phone-to-host-and-back, not full game latency.", color = muted, fontSize = 10.sp)
            }
        }
    }
}

private fun axisText(value: Float): String = (value * 100).roundToInt().toString()

private fun update(pressed: MutableList<String>, key: String, down: Boolean) {
    if (down && key !in pressed) pressed.add(key)
    if (!down) pressed.remove(key)
}

@Composable
private fun StickPad(label: String, axisX: Float, axisY: Float, modifier: Modifier, bg: Color, ink: Color, border: Color, onAxis: (Float, Float) -> Unit) {
    Column(modifier, horizontalAlignment = Alignment.CenterHorizontally) {
        Text(label, fontSize = 10.sp, color = muted, letterSpacing = 1.sp)
        Spacer(Modifier.height(8.dp))
        Box(
            Modifier.fillMaxWidth().aspectRatio(1f)
                .background(bg, CircleShape)
                .border(1.dp, border, CircleShape)
                .pointerInput(label) {
                    fun setAxis(position: Offset) {
                        val cx = size.width / 2f
                        val cy = size.height / 2f
                        val rangeX = (cx * 0.72f).coerceAtLeast(1f)
                        val rangeY = (cy * 0.72f).coerceAtLeast(1f)
                        onAxis(
                            ((position.x - cx) / rangeX).coerceIn(-1f, 1f),
                            ((cy - position.y) / rangeY).coerceIn(-1f, 1f)
                        )
                    }
                    detectDragGestures(
                        onDragStart = ::setAxis,
                        onDrag = { change, _ -> change.consume(); setAxis(change.position) },
                        onDragEnd = { onAxis(0f, 0f) },
                        onDragCancel = { onAxis(0f, 0f) }
                    )
                },
            contentAlignment = Alignment.Center
        ) {
            Box(Modifier.size(42.dp).offset(x = (axisX * 32f).dp, y = (-axisY * 32f).dp).background(ink, CircleShape))
        }
    }
}

@Composable
private fun PadButton(label: String, active: Boolean, ink: Color, surface: Color, border: Color, compact: Boolean = false, onInput: (Boolean) -> Unit) {
    Box(
        Modifier.padding(2.dp).size(if (compact) 44.dp else 48.dp)
            .background(if (active) ink else surface, RoundedCornerShape(12.dp))
            .border(1.dp, if (active) ink else border, RoundedCornerShape(12.dp))
            .pointerInput(label) {
                detectTapGestures(onPress = {
                    onInput(true)
                    try { tryAwaitRelease() } finally { onInput(false) }
                })
            },
        contentAlignment = Alignment.Center
    ) {
        Text(label, modifier = Modifier.padding(horizontal = 8.dp, vertical = 10.dp), fontSize = if (compact) 9.sp else 12.sp, color = if (active) surface else ink)
    }
}
