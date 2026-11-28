package com.simbuild.zeropad

import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.sin
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** The joystick, tested without a phone: finger position in, stick value out. */
class StickMathTest {
    private val radius = 100f

    @Test fun centreIsZero() {
        assertEquals(Stick.CENTRE, StickMath.fromTouch(0f, 0f, radius))
    }

    @Test fun restingThumbInsideDeadzoneIsZero() {
        assertEquals(Stick.CENTRE, StickMath.fromTouch(5f, -4f, radius))
    }

    @Test fun fullRightIsPlusOneAndZeroY() {
        val s = StickMath.fromTouch(100f, 0f, radius)
        assertEquals(1f, s.x, 1e-4f)
        assertEquals(0f, s.y, 1e-6f)
        assertTrue("no negative zero", 1f / s.y > 0f)
    }

    @Test fun pushingUpTheScreenIsPositiveY() {
        // Screen y grows downward, so a thumb ABOVE the centre has dy < 0.
        assertTrue(StickMath.fromTouch(0f, -60f, radius).y > 0f)
        assertTrue(StickMath.fromTouch(0f, 60f, radius).y < 0f)
    }

    @Test fun leftIsNegativeX() {
        assertTrue(StickMath.fromTouch(-60f, 0f, radius).x < 0f)
    }

    @Test fun fingerFarOutsideStillGivesExactlyFullDeflection() {
        val s = StickMath.fromTouch(900f, 0f, radius)
        assertEquals(1f, s.x, 1e-4f)
    }

    @Test fun diagonalIsNeverStrongerThanStraight() {
        val s = StickMath.fromTouch(100f, -100f, radius) // far into the corner of the square
        assertEquals(1f, hypot(s.x, s.y), 1e-4f)
        assertEquals(s.x, s.y, 1e-4f) // still exactly 45 degrees
    }

    @Test fun valueStartsAtZeroRightAfterTheDeadzone() {
        val justOut = StickMath.fromTouch(StickMath.DEADZONE * radius + 0.5f, 0f, radius)
        assertTrue(justOut.x in 0f..0.02f)
    }

    @Test fun fullCircleSweepKeepsDirectionAndNeverExceedsOne() {
        var degrees = 0
        while (degrees < 360) {
            val a = degrees * PI / 180
            for (r in listOf(30f, 60f, 100f, 140f)) {
                val dx = (r * cos(a)).toFloat()
                val dy = (-r * sin(a)).toFloat() // finger at angle a, counter-clockwise on screen
                val s = StickMath.fromTouch(dx, dy, radius)
                val length = hypot(s.x, s.y)
                assertTrue("deg=$degrees r=$r length=$length", length <= 1.0001f)
                val expected = ((minOf(r, radius) / radius) - StickMath.DEADZONE) / (1f - StickMath.DEADZONE)
                assertEquals("deg=$degrees r=$r", expected, length, 1e-3f)
                var diff = abs(atan2(s.y.toDouble(), s.x.toDouble()) - a)
                if (diff > PI) diff = 2 * PI - diff
                assertTrue("direction deg=$degrees r=$r diff=$diff", diff < 1e-3)
            }
            degrees += 7
        }
    }

    @Test fun movementIsSmoothNoJumps() {
        var last = 0f
        var d = 0f
        while (d <= 120f) {
            val x = StickMath.fromTouch(d, 0f, radius).x
            assertTrue("monotonic at $d", x >= last)
            assertTrue("no jump at $d", x - last < 0.03f)
            last = x
            d += 1f
        }
    }

    @Test fun zeroRadiusDoesNotCrash() {
        assertEquals(Stick.CENTRE, StickMath.fromTouch(10f, 10f, 0f))
    }

    @Test fun knobStaysInsideTheCircle() {
        val (x, y) = StickMath.knobOffset(300f, -400f, radius)
        assertEquals(100f, hypot(x, y), 1e-3f)
        assertEquals(Pair(30f, 40f), StickMath.knobOffset(30f, 40f, radius))
    }
}
