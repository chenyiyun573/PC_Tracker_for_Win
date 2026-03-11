using System.Drawing;
using System.Windows.Forms;

namespace PcTrackerNative.App.Services.Capture;

public sealed class GdiScreenCaptureService : IScreenCaptureService
{
    private readonly object _sync = new();
    private readonly int _intervalMs;
    private System.Threading.Timer? _timer;
    private Bitmap? _latestBitmap;

    public GdiScreenCaptureService(int intervalMs)
    {
        _intervalMs = Math.Max(50, intervalMs);
    }

    public string BackendName => "GDI fallback";

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        CaptureNow();
        _timer = new System.Threading.Timer(_ => CaptureNow(), null, _intervalMs, _intervalMs);
    }

    public void Stop()
    {
        IsRunning = false;
        _timer?.Dispose();
        _timer = null;
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

    private void CaptureNow()
    {
        if (!IsRunning)
        {
            return;
        }

        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        lock (_sync)
        {
            _latestBitmap?.Dispose();
            _latestBitmap = bitmap;
        }
    }
}
