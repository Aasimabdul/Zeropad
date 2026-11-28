package com.simbuild.zeropad

import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.BufferedWriter
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.util.Base64
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

internal enum class TransportMode { WIFI, USB }

internal data class GamepadSnapshot(
    val buttons: Set<String>,
    val leftX: Float,
    val leftY: Float,
    val rightX: Float,
    val rightY: Float,
    val leftTrigger: Float,
    val rightTrigger: Float
)

internal data class AckResult(val received: Boolean, val roundTripMs: Long?, val virtualOutput: Boolean = false)

/** The wire format from protocol/README.md, with no sockets, so it can be unit-tested. */
internal object Protocol {
    fun hmac(secret: ByteArray, data: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(secret, "HmacSHA256"))
        return mac.doFinal(data)
    }

    fun payload(sequence: Long, s: GamepadSnapshot): String = JSONObject()
        .put("version", 1)
        .put("type", "state")
        .put("sequence", sequence)
        .put("buttons", JSONArray(s.buttons.sorted()))
        .put("axes", JSONObject()
            .put("leftX", s.leftX.toDouble())
            .put("leftY", s.leftY.toDouble())
            .put("rightX", s.rightX.toDouble())
            .put("rightY", s.rightY.toDouble()))
        .put("triggers", JSONObject()
            .put("left", s.leftTrigger.toDouble())
            .put("right", s.rightTrigger.toDouble()))
        .toString()

    /** Signs `session \n sentAt \n base64(payload)` with the pairing key. */
    fun envelope(key: ByteArray, session: String, sentAt: Long, payloadJson: String): String {
        val payload64 = Base64.getEncoder().encodeToString(payloadJson.toByteArray(Charsets.UTF_8))
        val mac = hmac(key, "$session\n$sentAt\n$payload64".toByteArray(Charsets.UTF_8))
        return JSONObject()
            .put("session", session)
            .put("sentAt", sentAt)
            .put("payload", payload64)
            .put("mac", Base64.getEncoder().encodeToString(mac))
            .toString()
    }

    fun ackMac(key: ByteArray, session: String, sequence: Long, virtualOutput: Boolean): ByteArray =
        hmac(key, "ack\n$session\n$sequence\n$virtualOutput".toByteArray(Charsets.UTF_8))
}

internal class GamepadTransportClient {
    companion object {
        const val WIFI_PORT = 26760
        const val USB_PORT = 26761
        private const val MAX_DATAGRAM_BYTES = 4096
    }

    private var udp: DatagramSocket? = null
    private var tcp: Socket? = null
    private var tcpReader: BufferedReader? = null
    private var tcpWriter: BufferedWriter? = null
    private var address: InetAddress? = null
    private var port = WIFI_PORT
    private var key: ByteArray? = null
    private var mode: TransportMode? = null
    private var session = UUID.randomUUID().toString()
    private var sequence = 0L
    private val sendTimes = ConcurrentHashMap<Long, Long>()

    @Synchronized
    fun connect(host: String, pairingKey: String, selectedMode: TransportMode, portOverride: Int = 0) {
        close()
        val decodedKey = Base64.getMimeDecoder().decode(pairingKey.trim())
        require(decodedKey.size == 32) { "Pairing key must decode to 32 bytes." }

        if (selectedMode == TransportMode.USB) {
            val socket = Socket()
            try {
                socket.soTimeout = 250
                socket.tcpNoDelay = true
                socket.connect(InetSocketAddress("127.0.0.1", if (portOverride > 0) portOverride else USB_PORT), 1500)
                tcp = socket
                tcpReader = BufferedReader(InputStreamReader(socket.getInputStream(), Charsets.UTF_8))
                tcpWriter = BufferedWriter(OutputStreamWriter(socket.getOutputStream(), Charsets.UTF_8))
            } catch (error: Exception) {
                try { socket.close() } catch (_: Exception) { }
                throw error
            }
        } else {
            require(host.trim().isNotEmpty()) { "Enter the Windows host IP address." }
            val resolved = InetAddress.getByName(host.trim())
            port = if (portOverride > 0) portOverride else WIFI_PORT
            val socket = DatagramSocket()
            try {
                socket.soTimeout = 250
                socket.connect(resolved, port)
                udp = socket
                address = resolved
            } catch (error: Exception) {
                socket.close()
                throw error
            }
        }

        key = decodedKey
        mode = selectedMode
        session = UUID.randomUUID().toString()
        sequence = 0L
        sendTimes.clear()
    }

    @Synchronized
    fun send(snapshot: GamepadSnapshot) {
        val secret = key ?: return
        val currentSequence = sequence++
        val now = System.nanoTime()
        sendTimes.entries.removeIf { now - it.value > 5_000_000_000L }
        sendTimes[currentSequence] = now

        val envelope = Protocol.envelope(secret, session, System.currentTimeMillis(), Protocol.payload(currentSequence, snapshot))
        val bytes = envelope.toByteArray(Charsets.UTF_8)
        require(bytes.size <= MAX_DATAGRAM_BYTES) { "Controller message is too large." }

        when (mode) {
            TransportMode.WIFI -> {
                val socket = udp ?: return
                socket.send(DatagramPacket(bytes, bytes.size, address, port))
            }
            TransportMode.USB -> {
                val writer = tcpWriter ?: return
                writer.write(envelope)
                writer.newLine()
                writer.flush()
            }
            null -> Unit
        }
    }

    fun receiveAck(): AckResult {
        val secret = key ?: return AckResult(false, null)
        return try {
            val ackText = when (mode) {
                TransportMode.WIFI -> {
                    val socket = udp ?: return AckResult(false, null)
                    val bytes = ByteArray(MAX_DATAGRAM_BYTES)
                    val packet = DatagramPacket(bytes, bytes.size)
                    socket.receive(packet)
                    String(packet.data, packet.offset, packet.length, Charsets.UTF_8)
                }
                TransportMode.USB -> tcpReader?.readLine() ?: return AckResult(false, null)
                null -> return AckResult(false, null)
            }
            val ack = JSONObject(ackText)
            if (ack.optString("type") != "ack" || ack.optString("session") != session) return AckResult(false, null)
            val ackSequence = ack.optLong("sequence", -1L)
            val virtualOutput = ack.optBoolean("virtualOutput", false)
            val supplied = Base64.getMimeDecoder().decode(ack.optString("mac"))
            val expected = Protocol.ackMac(secret, session, ackSequence, virtualOutput)
            if (!java.security.MessageDigest.isEqual(supplied, expected)) return AckResult(false, null)
            val sentNanos = sendTimes.remove(ackSequence)
            val rtt = sentNanos?.let { ((System.nanoTime() - it) / 1_000_000L).coerceAtLeast(0L) }
            AckResult(true, rtt, virtualOutput)
        } catch (_: java.net.SocketTimeoutException) {
            AckResult(false, null)
        } catch (_: Exception) {
            AckResult(false, null)
        }
    }

    @Synchronized
    fun close() {
        try { tcp?.close() } catch (_: Exception) { }
        try { udp?.close() } catch (_: Exception) { }
        tcp = null
        tcpReader = null
        tcpWriter = null
        udp = null
        address = null
        key = null
        mode = null
        sendTimes.clear()
    }
}
