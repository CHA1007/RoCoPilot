using RocoPilot.Input;

namespace RocoPilot.Scripting;

public sealed class StrokeReplayer
{
    private const ushort KeyUp = 0x001;

    private const ushort MouseLeftDown = 0x001;
    private const ushort MouseLeftUp = 0x002;
    private const ushort MouseRightDown = 0x004;
    private const ushort MouseRightUp = 0x008;
    private const ushort MouseMiddleDown = 0x010;
    private const ushort MouseMiddleUp = 0x020;

    public async Task ReplayAsync(
        IInputDriver driver,
        RecordedScript script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(script);

        var strokes = script.Strokes;
        var pendingDowns = new List<PendingDown>();
        long previous = 0;

        foreach (var stroke in strokes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var delayMs = stroke.OffsetMs - previous;
            if (delayMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
            }

            previous = stroke.OffsetMs;
            driver.SendRawStroke(stroke.ToRaw());
            TrackPending(stroke, pendingDowns);
        }

        foreach (var down in pendingDowns)
        {
            if (ReleaseFor(down) is { } release)
                driver.SendRawStroke(release);
        }
    }

    private static void TrackPending(TimedStroke stroke, List<PendingDown> pendingDowns)
    {
        if (stroke.Kind == ReceivedDeviceKind.Keyboard)
        {
            if (stroke.State == 0)
            {
                if (!pendingDowns.Any(p => p.Kind == ReceivedDeviceKind.Keyboard && p.Code == stroke.Code))
                    pendingDowns.Add(new PendingDown(ReceivedDeviceKind.Keyboard, stroke.Code));
            }
            else
            {
                pendingDowns.RemoveAll(p => p.Kind == ReceivedDeviceKind.Keyboard && p.Code == stroke.Code);
            }
            return;
        }

        if (MouseDownFor(stroke.State) is { } downState)
        {
            var key = MouseKeyFor(downState);
            if (!pendingDowns.Any(p => p.Kind == ReceivedDeviceKind.Mouse && p.Code == key))
                pendingDowns.Add(new PendingDown(ReceivedDeviceKind.Mouse, key));
        }
        else if (MouseUpFor(stroke.State) is { } upState)
        {
            pendingDowns.RemoveAll(p => p.Kind == ReceivedDeviceKind.Mouse && p.Code == MouseKeyFor(upState));
        }
    }

    private static ReceivedStroke? ReleaseFor(PendingDown down)
    {
        if (down.Kind == ReceivedDeviceKind.Keyboard)
            return ReceivedStroke.Key(down.Code, KeyUp);

        var upState = down.Code switch
        {
            0 => MouseLeftUp,
            1 => MouseRightUp,
            2 => MouseMiddleUp,
            _ => (ushort?)null,
        };
        return upState is null ? null : ReceivedStroke.Mouse(upState.Value, 0, 0, 0, 0);
    }

    private static ushort? MouseDownFor(ushort state) => state switch
    {
        MouseLeftDown or MouseRightDown or MouseMiddleDown => state,
        _ => null,
    };

    private static ushort? MouseUpFor(ushort state) => state switch
    {
        MouseLeftUp or MouseRightUp or MouseMiddleUp => state,
        _ => null,
    };

    private static ushort MouseKeyFor(ushort state) => state switch
    {
        MouseLeftDown or MouseLeftUp => 0,
        MouseRightDown or MouseRightUp => 1,
        _ => 2,
    };

    private sealed record PendingDown(ReceivedDeviceKind Kind, ushort Code);
}