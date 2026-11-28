namespace ZeroPad.Server;

/// <summary>Where controller state goes: the ViGEm virtual Xbox pad in the real app, a recorder in tests.</summary>
public interface IPadSink : IDisposable
{
    void Apply(XInputReport report);
    void Reset();
}
