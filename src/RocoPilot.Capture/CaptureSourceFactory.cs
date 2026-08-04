using System.Text;

namespace RocoPilot.Capture;

public static class CaptureSourceFactory
{
    public static Task<ICaptureSource> StartBestAvailableAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return StartAsync(options, BuildDefaultStages(options), cancellationToken);
    }

    internal static async Task<ICaptureSource> StartAsync(
        CaptureOptions options,
        IReadOnlyList<Func<ICaptureSource>> stages,
        CancellationToken cancellationToken)
    {
        var failures = new StringBuilder();
        foreach (var stage in stages)
        {
            ICaptureSource? source = null;
            try
            {
                source = stage();
                await source.StartAsync(cancellationToken);
                return source;
            }
            catch (OperationCanceledException)
            {
                source?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                failures.Append("    - ").Append(source?.BackendName ?? "?")
                    .Append('：').AppendLine(ex.GetBaseException().Message);
                source?.Dispose();
            }
        }

        throw new CaptureException($"所有捕获后端均不可用：{Environment.NewLine}{failures}");
    }

    internal static IReadOnlyList<Func<ICaptureSource>> BuildDefaultStages(CaptureOptions options) => options.Backend switch
    {
        CaptureBackendMode.ForceWgcWindow => [() => WgcWindowOrThrow(options)],
        CaptureBackendMode.ForceWgcMonitor => [() => new WgcCaptureSource(WgcTarget.PrimaryMonitor(options.FpsWindow, options.FirstFrameTimeout))],
        CaptureBackendMode.ForceGdi => [() => new GdiCaptureSource(options.FpsWindow)],
        CaptureBackendMode.BitBlt => [() => new GdiCaptureSource(options.FpsWindow)],
        CaptureBackendMode.Wgc => WgcStages(options),
        _ => AutoStages(options),
    };

    private static IReadOnlyList<Func<ICaptureSource>> WgcStages(CaptureOptions options)
    {
        var stages = new List<Func<ICaptureSource>>(2);
        if (!string.IsNullOrWhiteSpace(options.WindowTitleSubstring))
        {
            stages.Add(() => WgcWindowOrThrow(options));
        }

        stages.Add(() => new WgcCaptureSource(WgcTarget.PrimaryMonitor(options.FpsWindow, options.FirstFrameTimeout)));
        return stages;
    }

    private static IReadOnlyList<Func<ICaptureSource>> AutoStages(CaptureOptions options)
    {
        var stages = new List<Func<ICaptureSource>>(3);
        if (!string.IsNullOrWhiteSpace(options.WindowTitleSubstring))
        {
            stages.Add(() => WgcWindowOrThrow(options));
        }

        stages.Add(() => new WgcCaptureSource(WgcTarget.PrimaryMonitor(options.FpsWindow, options.FirstFrameTimeout)));
        stages.Add(() => new GdiCaptureSource(options.FpsWindow));
        return stages;
    }

    private static ICaptureSource WgcWindowOrThrow(CaptureOptions options)
    {
        var hwnd = WindowFinder.FindFirstByTitleSubstring(options.WindowTitleSubstring);
        if (hwnd == IntPtr.Zero)
        {
            throw new CaptureException($"没有标题含「{options.WindowTitleSubstring}」的可见窗口");
        }

        return new WgcCaptureSource(WgcTarget.Window(hwnd, options.FpsWindow, options.FirstFrameTimeout));
    }
}
