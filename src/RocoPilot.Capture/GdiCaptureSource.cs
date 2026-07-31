using System.Buffers;

namespace RocoPilot.Capture;

public sealed class GdiCaptureSource : CaptureSourceCore
{
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _loopCts;
    private Task? _loop;

    public GdiCaptureSource(TimeSpan? fpsWindow = null)
        : base(CaptureBackends.GdiMonitor, "主显示器整屏", fpsWindow)
    {
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        if (width <= 0 || height <= 0)
        {
            throw new CaptureException($"取主显示器尺寸失败（{width}×{height}）");
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loopCts.Token;
        _loop = Task.Run(() => CaptureLoopAsync(width, height, token), CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var completed = await Task.WhenAny(_firstFrame.Task, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token));
        if (completed != _firstFrame.Task)
        {
            await _loopCts.CancelAsync();
            throw new CaptureException("GDI 2s 内没有帧");
        }
    }

    public override void Stop()
    {
        try
        {
            _loopCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    protected override void ReleaseBackend()
    {
        Stop();
        try
        {
            _loop?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
        }

        _loopCts?.Dispose();
        _loopCts = null;
    }

    private async Task CaptureLoopAsync(int width, int height, CancellationToken token)
    {
        var dib = new GdiDibSection(width, height);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var bytes = dib.Grab();
                PublishFrame(bytes, width, height);
                _firstFrame.TrySetResult();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            RaiseStopped("GDI 截取失败", ex);
        }
        finally
        {
            dib.Dispose();
            _firstFrame.TrySetResult();
        }
    }

    private sealed unsafe class GdiDibSection : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly IntPtr _bits;
        private IntPtr _screenDc;
        private IntPtr _memDc;
        private IntPtr _bitmap;
        private IntPtr _previousObject;

        public GdiDibSection(int width, int height)
        {
            _width = width;
            _height = height;

            _screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (_screenDc == IntPtr.Zero)
            {
                throw new CaptureException("GetDC(桌面) 失败");
            }

            _memDc = NativeMethods.CreateCompatibleDC(_screenDc);
            if (_memDc == IntPtr.Zero)
            {
                throw new CaptureException("CreateCompatibleDC 失败");
            }

            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = sizeof(NativeMethods.BITMAPINFOHEADER),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB,
                },
            };
            _bitmap = NativeMethods.CreateDIBSection(_memDc, in info, NativeMethods.DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero)
            {
                throw new CaptureException("CreateDIBSection 失败");
            }

            _previousObject = NativeMethods.SelectObject(_memDc, _bitmap);
        }

        public byte[] Grab()
        {
            if (!NativeMethods.BitBlt(_memDc, 0, 0, _width, _height, _screenDc, 0, 0, NativeMethods.SRCCOPY))
            {
                throw new CaptureException("BitBlt 失败");
            }

            var length = _width * _height * 4;
            var bytes = ArrayPool<byte>.Shared.Rent(length);
            fixed (byte* destination = bytes)
            {
                System.Buffer.MemoryCopy((void*)_bits, destination, length, length);
            }

            return bytes;
        }

        public void Dispose()
        {
            if (_previousObject != IntPtr.Zero)
            {
                NativeMethods.SelectObject(_memDc, _previousObject);
                _previousObject = IntPtr.Zero;
            }

            if (_bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(_bitmap);
                _bitmap = IntPtr.Zero;
            }

            if (_memDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
            }

            if (_screenDc != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
                _screenDc = IntPtr.Zero;
            }
        }
    }
}
