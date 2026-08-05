namespace RocoPilot.Input;

public interface IInputDriver : IDisposable
{
    string BackendName { get; }

    void Arm(TimeSpan timeout);

    void MoveRelative(int dx, int dy);

    void KeyDown(InputKey key);

    void KeyUp(InputKey key);

    void SendRawStroke(ReceivedStroke stroke);

    void StartStrokeRelay(TimeSpan discoveryTimeout, Action<ReceivedStroke> onStroke);

    void StopStrokeRelay();
}
