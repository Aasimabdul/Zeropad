package com.simbuild.zeropad

import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.util.UUID
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

internal data class GamepadSnapshot(
    val buttons: Set<String>,
    val leftX: Float,
    val leftY: Float,
    val rightX: Float,
    val rightY: Float,
    val leftTrigger: Float,
    val rightTrigger: Float
)

internal class UdpGamepadClient {
    companion object {
        const val PORT = 26760
        private const val MAX_DATAGRAM_BYTES = 4096
    }

    private var socket: DatagramSocket? = null
    private var address: InetAddress? = null
    private var key: ByteArray? = null
    private var session = UUID.randomUUID().toString()
    private var sequence = 0L

    @Synchronized
    fun connect(host: String, pairingKey: String) {
        close()
        val decodedKey = Base64.decode(pairingKey.trim(), Base64.DEFAULT)
        require(decodedKey.size == 32) { "Pairing key must decode to 32 bytes." }
        val resolved = InetAddress.getByName(host.trim())
        require(host.trim().isNotEmpty()) { "Enter the Windows host IP address." }
        val udp = DatagramSocket()
        udp.soTimeout = 250
        udp.connect(resolved, PORT)
        address = resolved
        key = decodedKey
        socket = udp
        session = UUID.randomUUID().toString()
        sequence = 0L
    }

    @Synchronized
    fun send(snapshot: GamepadSnapshot) {
        val udp = socket ?: return
        val secret = key ?: return
        val payload = JSONObject()
            .put("version", 1)
            .put("type", "state")
            .put("sequence", sequence++)
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
            .toByteArray(Charsets.UTF_8)

        require(envelope.size <= MAX_DATAGRAM_BYTES) { "Controller message is too large." }
        udp.send(DatagramPacket(envelope, envelope.size, address, PORT))
    }

    @Synchronized
    fun receiveAck(): Boolean {
        val udp = socket ?: return false
        val secret = key ?: return false
        return try {
            val bytes = ByteArray(MAX_DATAGRAM_BYTES)
            val packet = DatagramPacket(bytes, bytes.size)
            udp.receive(packet)
            val ack = JSONObject(String(packet.data, packet.offset, packet.length, Charsets.UTF_8))
            if (ack.optString("type") != "ack" || ack.optString("session") != session) return false
            val ackSequence = ack.optLong("sequence", -1L)
            val supplied = Base64.decode(ack.optString("mac"), Base64.DEFAULT)
            val expected = hmac(secret, "ack\n$session\n$ackSequence".toByteArray(Charsets.UTF_8))
            supplied.contentEquals(expected)
        } catch (_: java.net.SocketTimeoutException) {
            false
        } catch (_: Exception) {
            false
        }
    }

    @Synchronized
    fun close() {
        socket?.close()
        socket = null
        address = null
        key = null
    }

    private fun hmac(secret: ByteArray, data: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(secret, "HmacSHA256"))
        return mac.doFinal(data)
    }
}
