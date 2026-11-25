package com.simbuild.zeropad

import android.os.SystemClock
import android.util.Base64
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
    private var key: ByteArray? = null
    private var mode: TransportMode? = null
    private var session = UUID.randomUUID().toString()
    private var sequence = 0L
    private val sendTimes = ConcurrentHashMap<Long, Long>()

    @Synchronized
    fun connect(host: String, pairingKey: String, selectedMode: TransportMode) {
        close()
        val decodedKey = Base64.decode(pairingKey.trim(), Base64.DEFAULT)
        require(decodedKey.size == 32) { "Pairing key must decode to 32 bytes." }

        if (selectedMode == TransportMode.USB) {
            val socket = Socket()
            try {
                socket.soTimeout = 250
                socket.connect(InetSocketAddress("127.0.0.1", USB_PORT), 1500)
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
            val socket = DatagramSocket()
            try {
                socket.soTimeout = 250
                socket.connect(resolved, WIFI_PORT)
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
        val nowNanos = SystemClock.elapsedRealtimeNanos()
        sendTimes.entries.removeIf { nowNanos - it.value > 5_000_000_000L }
        sendTimes[currentSequence] = nowNanos

        val payload = JSONObject()
            .put("version", 1)
            .put("type", "state")
            .put("sequence", currentSequence)
            .put("buttons", JSONArray(snapshot.buttons.sorted()))
            .put("axes", JSONObject()
                .put("leftX", snapshot.leftX.toDouble())
                .put("leftY", snapshot.leftY.toDouble())
                .put("rightX", snapshot.rightX.toDouble())
                .put("rightY", snapshot.rightY.toDouble()))
            .put("triggers", JSONObject()
                .put("left", snapshot.leftTrigger.toDouble())
                .put("right", snapshot.rightTrigger.toDouble()))
            .toString()

        val payloadBase64 = Base64.encodeToString(payload.toByteArray(Charsets.UTF_8), Base64.NO_WRAP)
        val sentAt = System.currentTimeMillis()
        val signed = "$session\n$sentAt\n$payloadBase64".toByteArray(Charsets.UTF_8)
        val mac = hmac(secret, signed)
        val envelope = JSONObject()
            .put("session", session)
            .put("sentAt", sentAt)
            .put("payload", payloadBase64)
            .put("mac", Base64.encodeToString(mac, Base64.NO_WRAP))
            .toString()

        when (mode) {
            TransportMode.WIFI -> {
                val socket = udp ?: return
                val bytes = envelope.toByteArray(Charsets.UTF_8)
                require(bytes.size <= MAX_DATAGRAM_BYTES) { "Controller message is too large." }
                socket.send(DatagramPacket(bytes, bytes.size, address, WIFI_PORT))
            }
            TransportMode.USB -> {
                val writer = tcpWriter ?: return
                require(envelope.toByteArray(Charsets.UTF_8).size <= MAX_DATAGRAM_BYTES) { "Controller message is too large." }
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
            val supplied = Base64.decode(ack.optString("mac"), Base64.DEFAULT)
            val expected = hmac(secret, "ack\n$session\n$ackSequence\n${virtualOutput.toString()}".toByteArray(Charsets.UTF_8))
            if (!java.security.MessageDigest.isEqual(supplied, expected)) return AckResult(false, null)
            val sentNanos = sendTimes.remove(ackSequence)
            val rtt = sentNanos?.let { ((SystemClock.elapsedRealtimeNanos() - it) / 1_000_000L).coerceAtLeast(0L) }
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

    private fun hmac(secret: ByteArray, data: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(secret, "HmacSHA256"))
        return mac.doFinal(data)
    }
}
