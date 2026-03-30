using System.Drawing;
using PcTrackerNative.App.Models;
using PcTrackerNative.App.Services;
using PcTrackerNative.App.Services.Capture;
using PcTrackerNative.App.Services.Input;

namespace PcTrackerNative.App.Services.Tracking;

public sealed class TrackerController : IDisposable
{
    private readonly object _sync = new();
    private readonly RawInputMonitor _rawInputMonitor;
    private readonly EventAggregator _eventAggregator = new();

    private SessionRecorder? _recorder;
    private IScreenCaptureService? _captureService;
    private System.Threading.Timer? _maintenanceTimer;
    private SessionContext? _sessionContext;
    private TrackingOptions _options = new();
    private DateTimeOffset _lastInputTimestamp;
    private DateTimeOffset _lastWaitTimestamp;

    public TrackerController(RawInputMonitor rawInputMonitor)
    {
        _rawInputMonitor = rawInputMonitor;
        _rawInputMonitor.InputReceived += RawInputMonitorOnInputReceived;
        _eventAggregator.ActionReady += EventAggregatorOnActionReady;
    }

    public bool IsRecording { get; private set; }

    public async Task<SessionContext> StartAsync(TrackingOptions options, string outputRoot)
    {
        lock (_sync)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("A recording session is already active.");
            }
        }

        Directory.CreateDirectory(outputRoot);
        _options = options;
        var sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var sessionFolder = Path.Combine(outputRoot, sessionId);

        _sessionContext = new SessionContext
        {
            SessionId = sessionId,
            SessionFolder = sessionFolder,
            Mode = TrackingMode.NonTask,
            Options = new TrackingOptions
            {
                WaitIntervalSeconds = options.WaitIntervalSeconds,
                CaptureFrameIntervalMs = options.CaptureFrameIntervalMs,
                PreferModernCapture = options.PreferModernCapture,
            },
            StartedAt = DateTimeOffset.Now,
        };

        _captureService = CreateCaptureService(options);
        _sessionContext.CaptureBackend = _captureService.BackendName;
        _recorder = new SessionRecorder(_sessionContext);

        _rawInputMonitor.Start();
        _lastInputTimestamp = DateTimeOffset.Now;
        _lastWaitTimestamp = _lastInputTimestamp;
        IsRecording = true;
        _maintenanceTimer = new System.Threading.Timer(OnMaintenanceTick, null, 250, 250);

        await WaitForFirstFrameAsync();
        EnqueueDirectAction(new ActionRecordDescriptor
        {
            Timestamp = DateTimeOffset.Now,
            Type = "start",
            Description = "Started recording",
        });

        AppLogger.Info($"Recording started. SessionId={sessionId}; Output={sessionFolder}; Capture={_sessionContext.CaptureBackend}");
        return _sessionContext;
    }

    public async Task<SessionDraft> StopAsync(SessionOutcome outcome)
    {
        SessionRecorder? recorder;
        SessionContext? context;
        IScreenCaptureService? capture;

        lock (_sync)
        {
            if (!IsRecording || _recorder is null || _sessionContext is null)
            {
                throw new InvalidOperationException("No recording session is active.");
            }

            _rawInputMonitor.Stop();
            _maintenanceTimer?.Dispose();
            _maintenanceTimer = null;
            SafeFlushBufferedEvents();

            var stopText = outcome switch
            {
                SessionOutcome.Failed => "Marked recording as failed",
                SessionOutcome.Discarded => "Discarded recording",
                _ => "Finished recording",
            };

            EnqueueDirectAction(new ActionRecordDescriptor
            {
                Timestamp = DateTimeOffset.Now,
                Type = outcome == SessionOutcome.Failed ? "fail" : outcome == SessionOutcome.Discarded ? "discard" : "finish",
                Description = stopText,
            });

            IsRecording = false;
            recorder = _recorder;
            context = _sessionContext;
            capture = _captureService;

            _recorder = null;
            _sessionContext = null;
            _captureService = null;
        }

        context.EndedAt = DateTimeOffset.Now;

        try
        {
            capture?.Stop();
            capture?.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Capture service shutdown failed.", ex);
        }

        await recorder.CompleteAsync();
        AppLogger.Info($"Recording stopped. SessionId={context.SessionId}; Outcome={outcome}");
        return recorder.BuildDraft(context);
    }

    public Task CommitDraftAsync(SessionDraft draft, SessionSaveInfo saveInfo)
    {
        return SessionArtifactWriter.WriteAsync(draft, saveInfo);
    }

    public Task DiscardDraftAsync(SessionDraft draft)
    {
        return SessionArtifactWriter.DiscardAsync(draft);
    }

    public void Dispose()
    {
        _maintenanceTimer?.Dispose();
        _captureService?.Dispose();
        _rawInputMonitor.InputReceived -= RawInputMonitorOnInputReceived;
        _eventAggregator.ActionReady -= EventAggregatorOnActionReady;
    }

    private void RawInputMonitorOnInputReceived(object? sender, InputEvent e)
    {
        try
        {
            lock (_sync)
            {
                if (!IsRecording)
                {
                    return;
                }

                _lastInputTimestamp = e.Timestamp;
                _eventAggregator.Handle(e);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Raw input processing failed.", ex);
        }
    }

    private void EventAggregatorOnActionReady(ActionRecordDescriptor descriptor)
    {
        try
        {
            lock (_sync)
            {
                if (!IsRecording)
                {
                    return;
                }

                EnqueueDirectAction(descriptor);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Action aggregation failed.", ex);
        }
    }

    private void OnMaintenanceTick(object? state)
    {
        try
        {
            lock (_sync)
            {
                if (!IsRecording)
                {
                    return;
                }

                var now = DateTimeOffset.Now;
                _eventAggregator.Tick(now);

                var waitInterval = TimeSpan.FromSeconds(Math.Max(1, _options.WaitIntervalSeconds));
                if (now - _lastInputTimestamp >= waitInterval && now - _lastWaitTimestamp >= waitInterval)
                {
                    EnqueueDirectAction(new ActionRecordDescriptor
                    {
                        Timestamp = now,
                        Type = "wait",
                        Description = $"Waited about {_options.WaitIntervalSeconds} second(s)",
                        WaitSeconds = _options.WaitIntervalSeconds,
                    });
                    _lastWaitTimestamp = now;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Maintenance timer failed.", ex);
        }
    }

    private void SafeFlushBufferedEvents()
    {
        try
        {
            _eventAggregator.FlushAll();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to flush buffered events on stop.", ex);
        }
    }

    private void EnqueueDirectAction(ActionRecordDescriptor descriptor)
    {
        if (_recorder is null)
        {
            return;
        }

        Bitmap? screenshot = null;
        try
        {
            screenshot = _captureService?.GetLatestFrameClone();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to clone latest capture frame.", ex);
            screenshot?.Dispose();
            screenshot = null;
        }

        try
        {
            _recorder.Enqueue(descriptor, screenshot);
            screenshot = null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to enqueue recorder item.", ex);
        }
        finally
        {
            screenshot?.Dispose();
        }
    }

    private IScreenCaptureService CreateCaptureService(TrackingOptions options)
    {
        if (options.PreferModernCapture)
        {
            try
            {
                var modern = new WindowsGraphicsCaptureService(options.CaptureFrameIntervalMs);
                modern.Start();
                return modern;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Windows.Graphics.Capture failed to start. Falling back to GDI.", ex);
            }
        }

        var fallback = new GdiScreenCaptureService(options.CaptureFrameIntervalMs);
        fallback.Start();
        return fallback;
    }

    private async Task WaitForFirstFrameAsync()
    {
        for (var i = 0; i < 8; i++)
        {
            Bitmap? bitmap = null;
            try
            {
                bitmap = _captureService?.GetLatestFrameClone();
                if (bitmap is not null)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Waiting for first frame failed.", ex);
            }
            finally
            {
                bitmap?.Dispose();
            }

            await Task.Delay(100);
        }
    }
}
