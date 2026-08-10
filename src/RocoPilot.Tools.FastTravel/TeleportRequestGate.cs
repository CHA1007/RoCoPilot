namespace RocoPilot.Tools.FastTravel;

public sealed class TeleportRequestGate
{
    private volatile bool _pending;

    public bool Pending => _pending;

    public void Request() => _pending = true;

    public void Consume() => _pending = false;
}