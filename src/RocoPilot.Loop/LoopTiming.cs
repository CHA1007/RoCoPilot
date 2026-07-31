namespace RocoPilot.Loop;

public static class LoopTiming
{
    private const int CancelPollChunkMs = 100;

    public static void Sleep(int milliseconds, CancellationToken cancellationToken)
    {
        var remaining = milliseconds;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = Math.Min(remaining, CancelPollChunkMs);
            Thread.Sleep(chunk);
            remaining -= chunk;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
