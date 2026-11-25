package com.simbuild.zeropad

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

private val Paper = Color(0xFFF7F7F5)
private val Ink = Color(0xFF111111)
private val Muted = Color(0xFF777777)

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
    Column(Modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("ZeroPad", fontSize = 28.sp, color = Ink)
                Text("VIRTUAL GAMEPAD", fontSize = 11.sp, color = Muted)
            }
            Text("NOT CONNECTED", fontSize = 10.sp, color = Muted)
        }
        Text("Controller", fontSize = 18.sp, color = Ink)
        Text("Local touch preview · transport not connected", fontSize = 12.sp, color = Muted)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            StickPad("LEFT STICK")
            StickPad("RIGHT STICK")
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                PadButton("UP", pressed.contains("UP")) { update(pressed, "UP", it) }
                Row {
                    PadButton("LEFT", pressed.contains("LEFT")) { update(pressed, "LEFT", it) }
                    PadButton("DOWN", pressed.contains("DOWN")) { update(pressed, "DOWN", it) }
                    PadButton("RIGHT", pressed.contains("RIGHT")) { update(pressed, "RIGHT", it) }
                }
            }
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Row {
                    listOf("Y", "X").forEach { key -> PadButton(key, pressed.contains(key)) { update(pressed, key, it) } }
                }
                Row {
                    listOf("X", "A", "B").forEach { key -> PadButton(key, pressed.contains(key)) { update(pressed, key, it) } }
                }
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            listOf("LB", "LT", "SELECT", "START", "RT", "RB").forEach { key ->
                PadButton(key, pressed.contains(key)) { update(pressed, key, it) }
            }
        }
        Text("Pressed: " + (pressed.joinToString().ifEmpty { "None" }), fontSize = 12.sp, color = Muted)
        Text("Preview only. Networking and virtual Xbox output are not implemented.", fontSize = 11.sp, color = Muted)
    }
}

private fun update(pressed: MutableList<String>, key: String, down: Boolean) {
    if (down && key !in pressed) pressed.add(key)
    if (!down) pressed.remove(key)
}

@Composable
private fun StickPad(label: String) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(label, fontSize = 10.sp, color = Muted)
        Spacer(Modifier.height(8.dp))
        Box(Modifier.size(94.dp).background(Color(0xFFE7E7E4), CircleShape), contentAlignment = Alignment.Center) {
            Box(Modifier.size(42.dp).background(Ink, CircleShape))
        }
    }
}

@Composable
private fun PadButton(label: String, active: Boolean, onInput: (Boolean) -> Unit) {
    Button(
        onClick = { onInput(!active) },
        modifier = Modifier.padding(2.dp),
        shape = RoundedCornerShape(9.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = if (active) Ink else Color.White,
            contentColor = if (active) Color.White else Ink
        ),
        contentPadding = PaddingValues(horizontal = 8.dp, vertical = 6.dp)
    ) { Text(label, fontSize = 10.sp) }
}
