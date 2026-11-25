package com.simbuild.zeropad

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
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
import kotlin.math.roundToInt

private val Paper = Color(0xFFF7F7F5)
private val Ink = Color(0xFF111111)
private val Muted = Color(0xFF777777)
private val Border = Color(0xFFD9D9D5)

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                Surface(Modifier.fillMaxSize(), color = Paper) { ControllerScreen() }
            }
        }
    }
}

@Composable
private fun ControllerScreen() {
    val pressed = remember { mutableStateListOf<String>() }
    var leftX by remember { mutableFloatStateOf(0f) }
    var leftY by remember { mutableFloatStateOf(0f) }
    var rightX by remember { mutableFloatStateOf(0f) }
    var rightY by remember { mutableFloatStateOf(0f) }

    Column(
        Modifier.fillMaxSize().padding(horizontal = 18.dp, vertical = 16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("ZeroPad", fontSize = 28.sp, color = Ink)
                Text("VIRTUAL GAMEPAD", fontSize = 11.sp, color = Muted, letterSpacing = 1.5.sp)
            }
            Text("OFFLINE", fontSize = 10.sp, color = Muted)
        }
        Box(Modifier.fillMaxWidth().height(1.dp).background(Border))
        Text("Controller preview", fontSize = 17.sp, color = Ink)
        Text("Press and hold buttons · drag the sticks", fontSize = 12.sp, color = Muted)

        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            StickPad("LEFT STICK", leftX, leftY, onAxis = { x, y -> leftX = x; leftY = y })
            StickPad("RIGHT STICK", rightX, rightY, onAxis = { x, y -> rightX = x; rightY = y })
        }

        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                PadButton("↑", pressed.contains("UP")) { update(pressed, "UP", it) }
                Row {
                    PadButton("←", pressed.contains("LEFT")) { update(pressed, "LEFT", it) }
                    PadButton("↓", pressed.contains("DOWN")) { update(pressed, "DOWN", it) }
                    PadButton("→", pressed.contains("RIGHT")) { update(pressed, "RIGHT", it) }
                }
            }
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                PadButton("Y", pressed.contains("Y")) { update(pressed, "Y", it) }
                Row {
                    PadButton("X", pressed.contains("X")) { update(pressed, "X", it) }
                    Spacer(Modifier.width(40.dp))
                    PadButton("B", pressed.contains("B")) { update(pressed, "B", it) }
                }
                PadButton("A", pressed.contains("A")) { update(pressed, "A", it) }
            }
        }

        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            listOf("LB", "LT", "SELECT", "START", "RT", "RB").forEach { key ->
                PadButton(key, pressed.contains(key)) { update(pressed, key, it) }
            }
        }

        Box(Modifier.fillMaxWidth().height(1.dp).background(Border))
        Text("Input diagnostics", fontSize = 14.sp, color = Ink)
        Text("Buttons: ${pressed.joinToString().ifEmpty { "none" }}", fontSize = 11.sp, color = Muted)
        Text("L: ${axisText(leftX)}, ${axisText(leftY)}    R: ${axisText(rightX)}, ${axisText(rightY)}", fontSize = 11.sp, color = Muted)
        Text("Preview only — network transport and virtual Xbox output are not implemented.", fontSize = 11.sp, color = Muted)
    }
}

private fun axisText(value: Float): String = (value * 100).roundToInt().toString()

private fun update(pressed: MutableList<String>, key: String, down: Boolean) {
    if (down && key !in pressed) pressed.add(key)
    if (!down) pressed.remove(key)
}

@Composable
private fun StickPad(label: String, axisX: Float, axisY: Float, onAxis: (Float, Float) -> Unit) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(label, fontSize = 10.sp, color = Muted, letterSpacing = 1.sp)
        Spacer(Modifier.height(8.dp))
        Box(
            Modifier.size(112.dp)
                .background(Color(0xFFECECE9), CircleShape)
                .border(1.dp, Border, CircleShape)
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
            Box(Modifier.size(42.dp).offset(x = (axisX * 32f).dp, y = (-axisY * 32f).dp).background(Ink, CircleShape))
        }
    }
}

@Composable
private fun PadButton(label: String, active: Boolean, onInput: (Boolean) -> Unit) {
    Box(
        Modifier.padding(3.dp)
            .defaultMinSize(minWidth = 38.dp, minHeight = 38.dp)
            .background(if (active) Ink else Color.White, RoundedCornerShape(10.dp))
            .border(1.dp, if (active) Ink else Border, RoundedCornerShape(10.dp))
            .pointerInput(label) {
                detectTapGestures(onPress = {
                    onInput(true)
                    try {
                        tryAwaitRelease()
                    } finally {
                        onInput(false)
                    }
                })
            },
        contentAlignment = Alignment.Center
    ) {
        Text(label, modifier = Modifier.padding(horizontal = 8.dp, vertical = 10.dp), fontSize = 11.sp, color = if (active) Color.White else Ink)
    }
}
