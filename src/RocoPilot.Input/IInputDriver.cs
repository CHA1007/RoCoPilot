namespace RocoPilot.Input;

public interface IInputDriver : IDisposable
{
    string BackendName { get; }

    void Arm(TimeSpan timeout);

    void MoveRelative(int dx, int dy);

    void KeyDown(InputKey key);

    void KeyUp(InputKey key);

    void StartStrokeRelay(TimeSpan discoveryTimeout, Action<ReceivedStroke> onStroke);

    void StopStrokeRelay();
}
