using System.Drawing;

namespace PcTrackerNative.App.Services.Capture;

public interface IScreenCaptureService : IDisposable
{
    string BackendName { get; }

    bool IsRunning { get; }

    void Start();

    void Stop();

    Bitmap? GetLatestFrameClone();
}
