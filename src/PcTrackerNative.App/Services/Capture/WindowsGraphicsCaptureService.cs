using System.Drawing;
using PcTrackerNative.App.Interop;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace PcTrackerNative.App.Services.Capture;

public sealed class WindowsGraphicsCaptureService : IScreenCaptureService
{
    private readonly object _sync = new();
    private readonly int _minFrameIntervalMs;
    private IDirect3DDevice? _device;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private Bitmap? _latestBitmap;
    private DateTime _lastFrameUtc = DateTime.MinValue;

    public WindowsGraphicsCaptureService(int minFrameIntervalMs)
    {
        _minFrameIntervalMs = Math.Max(33, minFrameIntervalMs);
    }

    public string BackendName => "Windows.Graphics.Capture";

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException("Windows.Graphics.Capture is not supported on this system.");
        }

        var hmon = NativeMethods.GetPrimaryMonitorHandle();
        if (hmon == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not resolve the primary monitor handle.");
        }

        _item = CaptureHelper.CreateItemForMonitor(hmon);
        _device = Direct3D11Helper.CreateDevice();
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _item.Size);
        _session = _framePool.CreateCaptureSession(_item);
        _framePool.FrameArrived += FramePoolOnFrameArrived;
        _session.StartCapture();
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        if (_framePool is not null)
        {
            _framePool.FrameArrived -= FramePoolOnFrameArrived;
        }

        _session?.Dispose();
        _session = null;
        _framePool?.Dispose();
        _framePool = null;
        _item = null;
        _device?.Dispose();
        _device = null;

        lock (_sync)
        {
            _latestBitmap?.Dispose();
            _latestBitmap = null;
        }
    }

    public Bitmap? GetLatestFrameClone()
    {
        lock (_sync)
        {
            return _latestBitmap is null ? null : new Bitmap(_latestBitmap);
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void FramePoolOnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (!IsRunning || _device is null)
        {
            return;
        }

        var shouldRecreate = false;
        var newSize = default(Windows.Graphics.SizeInt32);

        using var frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        if (frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastFrameUtc).TotalMilliseconds < _minFrameIntervalMs)
        {
            if (_item is not null && (frame.ContentSize.Width != _item.Size.Width || frame.ContentSize.Height != _item.Size.Height))
            {
                shouldRecreate = true;
                newSize = frame.ContentSize;
            }

            if (shouldRecreate && _framePool is not null)
            {
                _framePool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, newSize);
            }

            return;
        }

        if (_item is not null && (frame.ContentSize.Width != _item.Size.Width || frame.ContentSize.Height != _item.Size.Height))
        {
            shouldRecreate = true;
            newSize = frame.ContentSize;
        }

        var bitmap = frame.ToClonedBitmap();
        lock (_sync)
        {
            _latestBitmap?.Dispose();
            _latestBitmap = bitmap;
            _lastFrameUtc = now;
        }

        if (shouldRecreate && _framePool is not null)
        {
            _framePool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, newSize);
        }
    }
}
