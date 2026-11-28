package com.simbuild.zeropad

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.util.Base64
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ProtocolTest {
    private val key = ByteArray(32) { it.toByte() }
    private val keyBase64 = Base64.getEncoder().encodeToString(key)

    @Test fun goldenVectorsMatchTheWindowsCompanion() {
        // Same constants live in windows/ZeroPad.Server.Tests/HostTests.cs.
        val signed = "00000000-0000-0000-0000-000000000001\n1795630000000\neyJ2IjoxfQ=="
        assertEquals("h4Etjz3TmB668vST5Fs5EpIVWSE8viQINRB5HCAxF5s=",
            Base64.getEncoder().encodeToString(Protocol.hmac(key, signed.toByteArray())))
        assertEquals("7NwqBjtPUkGCAeJ8ztt1iCpG4b4xnh1c1ijuIP2UaFQ=",
            Base64.getEncoder().encodeToString(Protocol.ackMac(key, "00000000-0000-0000-0000-000000000001", 7, true)))
    }

    @Test fun payloadHasEveryFieldTheServerRequires() {
        val snap = GamepadSnapshot(setOf("B", "A"), -1f, 0.5f, 0f, 1f, 0f, 1f)
        val p = JSONObject(Protocol.payload(42, snap))
        assertEquals(1, p.getInt("version"))
        assertEquals("state", p.getString("type"))
        assertEquals(42L, p.getLong("sequence"))
        assertEquals("[\"A\",\"B\"]", p.getJSONArray("buttons").toString())
        assertEquals(-1.0, p.getJSONObject("axes").getDouble("leftX"), 0.0)
        assertEquals(0.5, p.getJSONObject("axes").getDouble("leftY"), 0.0)
        assertEquals(1.0, p.getJSONObject("axes").getDouble("rightY"), 0.0)
        assertEquals(1.0, p.getJSONObject("triggers").getDouble("right"), 0.0)
    }

    @Test fun envelopeIsSignedOverSessionTimeAndPayload() {
        val env = JSONObject(Protocol.envelope(key, "sess", 123L, "{\"v\":1}"))
        val payload = env.getString("payload")
        val expected = Protocol.hmac(key, "sess\n123\n$payload".toByteArray())
        assertEquals(Base64.getEncoder().encodeToString(expected), env.getString("mac"))
        assertEquals("{\"v\":1}", String(Base64.getDecoder().decode(payload)))
        assertTrue(env.toString().length < 4096)
    }

    /** A stand-in for the Windows companion: checks the MAC the way it does and answers with an ack. */
    private class FakeHost(val key: ByteArray, val virtualOutput: Boolean = true) : Thread() {
        val socket = DatagramSocket(0, InetAddress.getByName("127.0.0.1")).apply { soTimeout = 200 }
        @Volatile var running = true
        val states = java.util.Collections.synchronizedList(mutableListOf<JSONObject>())
        var rejected = 0
        override fun run() {
            val buf = ByteArray(4096)
            while (running) {
                val p = DatagramPacket(buf, buf.size)
                try { socket.receive(p) } catch (e: java.net.SocketTimeoutException) { continue } catch (e: Exception) { return }
                val env = JSONObject(String(p.data, 0, p.length))
                val mac = Protocol.hmac(key, "${env.getString("session")}\n${env.getLong("sentAt")}\n${env.getString("payload")}".toByteArray())
                if (Base64.getEncoder().encodeToString(mac) != env.getString("mac")) { rejected++; continue }
                val state = JSONObject(String(Base64.getDecoder().decode(env.getString("payload"))))
                states.add(state)
                val session = env.getString("session")
                val seq = state.getLong("sequence")
                val ack = JSONObject().put("type", "ack").put("session", session).put("sequence", seq)
                    .put("virtualOutput", virtualOutput)
                    .put("mac", Base64.getEncoder().encodeToString(Protocol.ackMac(key, session, seq, virtualOutput)))
                val out = ack.toString().toByteArray()
                socket.send(DatagramPacket(out, out.size, p.address, p.port))
            }
        }
        fun stopHost() { running = false; socket.close() }
    }

    @Test fun clientTalksToAHostOverRealUdp() {
        val host = FakeHost(key).also { it.start() }
        try {
            val client = GamepadTransportClient()
            client.connect("127.0.0.1", keyBase64, TransportMode.WIFI, host.socket.localPort)
            var last: AckResult? = null
            for (i in 0..9) {
                // a thumb dragging the left stick to the right edge
                client.send(GamepadSnapshot(emptySet(), i / 9f, 0f, 0f, 0f, 0f, 0f))
                last = client.receiveAck()
                assertTrue("ack $i", last.received)
            }
            assertTrue(last!!.virtualOutput)
            assertNotNull(last.roundTripMs)
            assertEquals(10, host.states.size)
            for (i in 0..9) {
                assertEquals(i.toLong(), host.states[i].getLong("sequence"))
                assertEquals(i / 9.0, host.states[i].getJSONObject("axes").getDouble("leftX"), 1e-6)
            }
            assertEquals(0, host.rejected)
            client.close()
        } finally { host.stopHost() }
    }

    @Test fun clientReportsVirtualDriverMissing() {
        val host = FakeHost(key, virtualOutput = false).also { it.start() }
        try {
            val client = GamepadTransportClient()
            client.connect("127.0.0.1", keyBase64, TransportMode.WIFI, host.socket.localPort)
            client.send(GamepadSnapshot(emptySet(), 0f, 0f, 0f, 0f, 0f, 0f))
            val ack = client.receiveAck()
            assertTrue(ack.received)
            assertFalse(ack.virtualOutput)
        } finally { host.stopHost() }
    }

    @Test fun clientRejectsAnAckSignedWithAnotherKey() {
        val host = FakeHost(ByteArray(32) { 7 }).also { it.start() }
        try {
            val client = GamepadTransportClient()
            client.connect("127.0.0.1", keyBase64, TransportMode.WIFI, host.socket.localPort)
            client.send(GamepadSnapshot(emptySet(), 1f, 0f, 0f, 0f, 0f, 0f))
            assertFalse(client.receiveAck().received)
            assertEquals(1, host.rejected) // host also refused our packet: keys differ
        } finally { host.stopHost() }
    }

    @Test fun badPairingKeyIsRefusedUpFront() {
        val client = GamepadTransportClient()
        try { client.connect("127.0.0.1", "AAAA", TransportMode.WIFI); org.junit.Assert.fail("expected failure") }
        catch (e: IllegalArgumentException) { /* expected */ }
    }
}
