using RocoPilot.Input;

namespace RocoPilot.Tools.Handpan.Tests;

internal class RecordingDriver : IInputDriver
{
    private readonly object _gate = new();

    public IReadOnlyList<(ushort Key, bool Down)> Strokes
    {
        get { lock (_gate) { return _strokes.ToList(); } }
    }

    public int ArmCount { get; private set; }

    public int DisposeCount { get; private set; }

    private readonly List<(ushort Key, bool Down)> _strokes = [];

    public string BackendName => "recording";

    public void Arm() => ArmCount++;

    public void MoveRelative(int dx, int dy)
    {
    }

    public virtual void KeyDown(InputKey key)
    {
        lock (_gate)
        {
            _strokes.Add((key.VirtualKey, true));
        }
    }

    public virtual void KeyUp(InputKey key)
    {
        lock (_gate)
        {
            _strokes.Add((key.VirtualKey, false));
        }
    }

    public void SendRawStroke(ReceivedStroke stroke)
    {
    }

    public void StartStrokeRelay(Action<ReceivedStroke> onStroke)
    {
    }

    public void StopStrokeRelay()
    {
    }

    public void Dispose() => DisposeCount++;
}
