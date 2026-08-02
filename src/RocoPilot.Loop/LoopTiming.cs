namespace RocoPilot.Loop;

public static class LoopTiming
{
    private const int CancelPollChunkMs = 100;
    internal const int RestabilizeTimeoutMs = 500;
    internal const int RestabilizePollMs = 30;

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
