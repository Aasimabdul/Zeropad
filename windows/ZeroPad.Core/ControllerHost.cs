using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZeroPad.Server;

/// <summary>
/// The brain of the companion, with no sockets and no driver: checks a packet, applies it to the
/// pad, builds the authenticated acknowledgement, and resets the pad when input stops.
/// </summary>
public sealed class ControllerHost
{
    public static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReplayMemory = TimeSpan.FromSeconds(30);

    private readonly byte[] _key;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    // Highest sequence seen per session. Packets are only valid for +-10 s, so remembering 30 s stops replays.
    private readonly Dictionary<Guid, (long Sequence, DateTimeOffset Seen)> _seen = new();
    private IPadSink? _sink;
    private Guid? _active;
    private DateTimeOffset _lastSeen = DateTimeOffset.MinValue;
    private bool _neutral = true;

    public ControllerHost(byte[] key, IPadSink? sink, Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        if (key.Length != 32) throw new ArgumentException("Pairing key must be 32 bytes.", nameof(key));
        _key = key;
        _sink = sink;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? (_ => { });
    }

    public bool VirtualOutput { get { lock (_gate) return _sink is not null; } }

    /// <summary>Validates one datagram / frame and returns the acknowledgement. Throws on anything not accepted.</summary>
    public byte[] Process(byte[] bytes)
    {
        var now = _clock();
        var packet = AuthenticatedPacket.Parse(bytes, _key, now);
        var state = packet.State;
        bool output;
        lock (_gate)
        {
            if (_seen.TryGetValue(packet.Session, out var known) && state.Sequence <= known.Sequence)
                throw new FormatException("Stale sequence.");
            if (_active is { } active && active != packet.Session && now - _lastSeen <= SessionIdleTimeout)
                throw new FormatException("Another controller session is active.");

            foreach (var old in _seen.Where(e => now - e.Value.Seen > ReplayMemory).Select(e => e.Key).ToList())
                _seen.Remove(old);
            _seen[packet.Session] = (state.Sequence, now);
            _active = packet.Session;
            _lastSeen = now;
            if (_sink is not null)
            {
                try { _sink.Apply(XInputMap.From(state)); }
                catch (Exception ex)
                {
                    _log($"Virtual controller failed: {ex.GetType().Name}; disabling virtual output.");
                    try { _sink.Dispose(); } catch { }
                    _sink = null;
                }
            }
            _neutral = false;
            output = _sink is not null;
        }

        var text = $"ack\n{packet.Session:D}\n{state.Sequence}\n{(output ? "true" : "false")}";
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(text));
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "ack",
            session = packet.Session.ToString("D"),
            sequence = state.Sequence,
            mac = Convert.ToBase64String(mac),
            virtualOutput = output
        });
    }

    /// <summary>Call about every 100 ms. If input stopped for a second, release every button and stick.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (_neutral || _clock() - _lastSeen <= InputTimeout) return;
            try { _sink?.Reset(); } catch { }
            _neutral = true;
            _active = null;
            _log("Input timeout: controller state reset.");
        }
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            try { _sink?.Reset(); } catch { }
            try { _sink?.Dispose(); } catch { }
            _sink = null;
        }
    }
}
