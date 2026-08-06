using System.Buffers;
using System.Runtime.InteropServices;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RocoPilot.Capture;

public sealed record WgcTarget
{
    private WgcTarget()
    {
    }

    internal IntPtr WindowHandle { get; init; }

    internal bool IsMonitor { get; init; }

    internal TimeSpan FpsWindow { get; init; } = CaptureDefaults.FpsWindow;

    internal TimeSpan FirstFrameTimeout { get; init; } = CaptureDefaults.FirstFrameTimeout;

    internal string Description { get; init; } = "";

    public static WgcTarget Window(IntPtr windowHandle, TimeSpan? fpsWindow = null, TimeSpan? firstFrameTimeout = null) => new()
    {
        WindowHandle = windowHandle,
        Description = WindowFinder.GetTitle(windowHandle) is { Length: > 0 } title ? title : $"HWND 0x{windowHandle:X}",
        FpsWindow = fpsWindow ?? CaptureDefaults.FpsWindow,
        FirstFrameTimeout = firstFrameTimeout ?? CaptureDefaults.FirstFrameTimeout,
    };

    public static WgcTarget PrimaryMonitor(TimeSpan? fpsWindow = null, TimeSpan? firstFrameTimeout = null) => new()
    {
        IsMonitor = true,
        Description = "主显示器整屏",
        FpsWindow = fpsWindow ?? CaptureDefaults.FpsWindow,
        FirstFrameTimeout = firstFrameTimeout ?? CaptureDefaults.FirstFrameTimeout,
    };
}

public sealed class WgcCaptureSource : CaptureSourceCore
{
    private readonly WgcTarget _target;
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _pipelineGate = new();

    private Vortice.Direct3D11.ID3D11Device? _d3dDevice;
    private Vortice.Direct3D11.ID3D11DeviceContext? _immediateContext;
    private IDirect3DDevice? _winRtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private Vortice.Direct3D11.ID3D11Texture2D? _staging;
    private int _poolWidth;
    private int _poolHeight;

    public WgcCaptureSource(WgcTarget target)
        : base(
            (target ?? throw new ArgumentNullException(nameof(target))).IsMonitor ? CaptureBackends.WgcMonitor : CaptureBackends.WgcWindow,
            target.Description,
            target.FpsWindow)
    {
        _target = target;
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            CreateDeviceChain();
            _item = _target.IsMonitor
                ? WgcInterop.CreateItemForPrimaryMonitor()
                : WgcInterop.CreateItemForWindow(_target.WindowHandle);

            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winRtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);
            _poolWidth = _item.Size.Width;
            _poolHeight = _item.Size.Height;
            _session = _pool.CreateCaptureSession(_item);
            if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            {
                _session.IsBorderRequired = false;
            }

            _item.Closed += OnItemClosed;
            _pool.FrameArrived += OnFrameArrived;
            _session.StartCapture();
        }
        catch
        {
            ReleaseBackend();
            throw;
        }

        using var timeout = new CancellationTokenSource(_target.FirstFrameTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var completed = await Task.WhenAny(_firstFrame.Task, Task.Delay(Timeout.InfiniteTimeSpan, linked.Token));
        if (completed == _firstFrame.Task)
        {
            return;
        }

        AbandonPipeline();
        cancellationToken.ThrowIfCancellationRequested();
        throw new CaptureException($"WGC {_target.FirstFrameTimeout.TotalSeconds:0}s 内没有帧（{SourceDescription}）");
    }

    public override void Stop() => ReleaseBackend();

    protected override void ReleaseBackend() => ReleasePipeline();

    private void CreateDeviceChain()
    {
        var result = D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            null,
            out var device);
        result.CheckError();
        _d3dDevice = device ?? throw new CaptureException("D3D11 设备创建失败");
        _immediateContext = _d3dDevice.ImmediateContext;

        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        _winRtDevice = WgcInterop.CreateWinRtDevice(dxgiDevice.NativePointer)
                       ?? throw new CaptureException("D3D11 → WinRT 设备换算失败");
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (_pipelineGate)
        {
            if (_pool is null)
            {
                return;
            }

            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null)
                {
                    return;
                }

                var size = frame.ContentSize;
                if (size.Width <= 0 || size.Height <= 0)
                {
                    return;
                }

                if (size.Width != _poolWidth || size.Height != _poolHeight)
                {
                    _pool!.Recreate(_winRtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                    _poolWidth = size.Width;
                    _poolHeight = size.Height;
                    _staging?.Dispose();
                    _staging = null;
                }

                var bytes = ReadFrameBytes(frame.Surface, size.Width, size.Height);
                PublishFrame(bytes, size.Width, size.Height);
                _firstFrame.TrySetResult();
            }
            catch (Exception ex)
            {
                RaiseStopped("帧处理失败", ex);
            }
        }
    }

    private unsafe byte[] ReadFrameBytes(IDirect3DSurface surface, int width, int height)
    {
        using var texture = WgcInterop.AsNativeTexture2D(surface);
        if (_staging is null)
        {
            _staging = _d3dDevice!.CreateTexture2D(new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
            });
        }

        var context = _immediateContext!;
        context.CopyResource(_staging, texture);
        var mapped = context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var length = width * height * 4;
            var bytes = ArrayPool<byte>.Shared.Rent(length);
            var rowBytes = width * 4;
            var source = (byte*)mapped.DataPointer;
            fixed (byte* destination = bytes)
            {
                for (var y = 0; y < height; y++)
                {
                    System.Buffer.MemoryCopy(source + (long)y * mapped.RowPitch, destination + (long)y * rowBytes, rowBytes, rowBytes);
                }
            }

            return bytes;
        }
        finally
        {
            context.Unmap(_staging, 0);
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args) =>
        RaiseStopped("捕获目标已关闭（窗口关闭 / 显示器断开）");

    private void AbandonPipeline()
    {
        lock (_pipelineGate)
        {
            if (_pool is not null)
            {
                _pool.FrameArrived -= OnFrameArrived;
                _pool.Dispose();
            }

            if (_session is not null)
            {
                _session.Dispose();
            }

            if (_item is not null)
            {
                _item.Closed -= OnItemClosed;
            }

            _session = null;
            _pool = null;
            _item = null;
            _staging?.Dispose();
            _staging = null;
            (_winRtDevice as IDisposable)?.Dispose();
            _winRtDevice = null;
            _immediateContext?.Dispose();
            _immediateContext = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
        }
    }

    private void ReleasePipeline()
    {
        lock (_pipelineGate)
        {
            if (_pool is not null)
            {
                _pool.FrameArrived -= OnFrameArrived;
            }

            if (_session is not null)
            {
                _session.Dispose();
                _session = null;
            }

            if (_pool is not null)
            {
                _pool.Dispose();
                _pool = null;
            }

            if (_item is not null)
            {
                _item.Closed -= OnItemClosed;
                _item = null;
            }

            _staging?.Dispose();
            _staging = null;
            (_winRtDevice as IDisposable)?.Dispose();
            _winRtDevice = null;
            _immediateContext?.Dispose();
            _immediateContext = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
            _firstFrame.TrySetResult();
        }
    }
}

internal static class WgcInterop
{
    private static readonly Guid IidGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport]
    [System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport]
    [System.Runtime.InteropServices.Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    public static IDirect3DDevice? CreateWinRtDevice(IntPtr dxgiDevicePointer)
    {
        var hresult = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePointer, out var abi);
        Marshal.ThrowExceptionForHR(hresult);
        return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(abi);
    }

    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
        var iid = IidGraphicsCaptureItem;
        var abi = interop.CreateForWindow(hwnd, ref iid);
        if (abi == IntPtr.Zero)
        {
            throw new CaptureException($"CreateForWindow 失败（HWND 0x{hwnd:X}）");
        }

        return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(abi)
               ?? throw new CaptureException($"CreateForWindow 返回空（HWND 0x{hwnd:X}）");
    }

    public static GraphicsCaptureItem CreateItemForPrimaryMonitor()
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(0, 0), NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        if (monitor == IntPtr.Zero)
        {
            throw new CaptureException("取主显示器句柄失败");
        }

        var item = GraphicsCaptureItem.TryCreateFromDisplayId(new Windows.Graphics.DisplayId((ulong)monitor));
        return item ?? throw new CaptureException("显示器级 CaptureItem 创建失败");
    }

    public static Vortice.Direct3D11.ID3D11Texture2D AsNativeTexture2D(IDirect3DSurface surface)
    {
        var native = ((WinRT.IWinRTObject)surface).NativeObject;
        var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(native.ThisPtr);
        var iid = IidTexture2D;
        var texturePointer = access.GetInterface(ref iid);
        if (texturePointer == IntPtr.Zero)
        {
            throw new CaptureException("帧面 → ID3D11Texture2D 换算失败");
        }

        return new Vortice.Direct3D11.ID3D11Texture2D(texturePointer);
    }
}
