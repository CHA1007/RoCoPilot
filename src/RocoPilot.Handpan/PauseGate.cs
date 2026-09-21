namespace RocoPilot.Handpan;

public enum PauseSource
{
    Manual,

    FocusLost,
}

public sealed class PauseGate : IDisposable
{
    private const int WaitSliceMs = 20;

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _open = new(true);
    private bool _manuallyHeld;
    private bool _heldForFocus;

    public bool IsOpen
    {
        get { lock (_gate) { return !_manuallyHeld && !_heldForFocus; } }
    }

    public bool IsManuallyHeld
    {
        get { lock (_gate) { return _manuallyHeld; } }
    }

    public bool IsHeldForFocus
    {
        get { lock (_gate) { return _heldForFocus; } }
    }

    public void HoldManually() => Hold(ref _manuallyHeld, true);

    public void ReleaseManually() => Hold(ref _manuallyHeld, false);

    public void HoldForFocusLoss() => Hold(ref _heldForFocus, true);

    public void ReleaseForFocusRegain() => Hold(ref _heldForFocus, false);

    public void WaitUntilOpen(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsOpen)
            {
                return;
            }

            _open.Wait(WaitSliceMs, cancellationToken);
        }
    }

    public void Dispose() => _open.Dispose();

    private void Hold(ref bool held, bool value)
    {
        lock (_gate)
        {
            held = value;
            if (_manuallyHeld || _heldForFocus)
            {
                _open.Reset();
            }
            else
            {
                _open.Set();
            }
        }
    }
}
