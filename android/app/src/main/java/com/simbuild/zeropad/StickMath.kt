package com.simbuild.zeropad

import kotlin.math.hypot
import kotlin.math.min

/** A stick position: -1..1 on both axes, +y is UP (same convention as XInput and the Windows companion). */
internal data class Stick(val x: Float, val y: Float) {
    companion object { val CENTRE = Stick(0f, 0f) }
}

/** Turns a thumb position into a stick value. Plain Kotlin, so it is unit-tested on the JVM. */
internal object StickMath {
    /** Fraction of full travel that is ignored around the centre, so a resting thumb does not drift. */
    const val DEADZONE = 0.08f

    /**
     * [dx]/[dy] are the thumb's distance from the stick centre in pixels (screen y grows DOWN),
     * [radius] is the distance that counts as full deflection.
     * The result is limited to the unit circle (a diagonal is never stronger than straight) and
     * rescaled so the value starts at 0 right after the dead zone and reaches 1 at the rim.
     */
    fun fromTouch(dx: Float, dy: Float, radius: Float, deadzone: Float = DEADZONE): Stick {
        if (radius <= 0f) return Stick.CENTRE
        val x = dx / radius
        val y = -dy / radius
        val length = hypot(x, y)
        if (length <= deadzone) return Stick.CENTRE
        val scaled = (min(length, 1f) - deadzone) / (1f - deadzone)
        val k = scaled / length
        return Stick(x * k + 0f, y * k + 0f) // "+ 0f" turns -0.0 into 0.0
    }

    /** Where to draw the knob: the finger position held inside the circle. Returns pixels from the centre (screen coordinates). */
    fun knobOffset(dx: Float, dy: Float, radius: Float): Pair<Float, Float> {
        val length = hypot(dx, dy)
        if (length <= radius || length == 0f) return Pair(dx, dy)
        val k = radius / length
        return Pair(dx * k, dy * k)
    }
}
