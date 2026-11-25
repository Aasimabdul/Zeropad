using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZeroPad.Server;

public sealed record AuthenticatedPacket(Guid Session, long SentAt, ControllerState State)
{
    public const int MaximumBytes = 4096;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    public static AuthenticatedPacket Parse(byte[] datagram, byte[] key, DateTimeOffset now)
    {
        if (datagram.Length == 0 || datagram.Length > MaximumBytes)
            throw new FormatException("Datagram size is invalid.");
        if (key.Length != 32) throw new ArgumentException("Pairing key must be 32 bytes.", nameof(key));

        using var document = JsonDocument.Parse(datagram, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Envelope must be an object.");

        var sessionText = RequiredString(root, "session");
        if (!Guid.TryParseExact(sessionText, "D", out var session)) throw new FormatException("Invalid session ID.");
        var sentAt = RequiredInt64(root, "sentAt");
        var payload = RequiredString(root, "payload");
        var suppliedMac = Convert.FromBase64String(RequiredString(root, "mac"));

        var nowMs = now.ToUnixTimeMilliseconds();
        var lifetimeMs = (long)Lifetime.TotalMilliseconds;
        if (sentAt < nowMs - lifetimeMs || sentAt > nowMs + lifetimeMs)
            throw new FormatException("Expired message.");

        var signedBytes = Encoding.UTF8.GetBytes($"{session:D}\n{sentAt}\n{payload}");
        var expectedMac = HMACSHA256.HashData(key, signedBytes);
        if (suppliedMac.Length != expectedMac.Length ||
            !CryptographicOperations.FixedTimeEquals(suppliedMac, expectedMac))
            throw new FormatException("Authentication failed.");

        var stateJson = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        var state = ControllerState.Parse(stateJson);
        return new AuthenticatedPacket(session, sentAt, state);
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new FormatException($"Missing or invalid field: {name}");
        return value.GetString()!;
    }

    private static long RequiredInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
            throw new FormatException($"Missing or invalid field: {name}");
        return number;
    }
}
