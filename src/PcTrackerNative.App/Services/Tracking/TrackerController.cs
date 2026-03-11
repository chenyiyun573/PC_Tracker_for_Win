using System.Drawing;
using PcTrackerNative.App.Models;
using PcTrackerNative.App.Services.Capture;
using PcTrackerNative.App.Services.Input;
using PcTrackerNative.App.Services;

namespace PcTrackerNative.App.Services.Tracking;

public sealed class TrackerController : IDisposable
{
    private readonly object _sync = new();
    private readonly RawInputMonitor _rawInputMonitor;
    private readonly string _eventsRoot;
    private readonly EventAggregator _eventAggregator = new();

    private SessionRecorder? _recorder;
    private IScreenCaptureService? _captureService;
    private System.Threading.Timer? _maintenanceTimer;
    private SessionContext? _sessionContext;
    private TrackingOptions _options = new();
    private DateTimeOffset _lastInputTimestamp;
    private DateTimeOffset _lastWaitTimestamp;

    public TrackerController(RawInputMonitor rawInputMonitor, string eventsRoot)
    {
        _rawInputMonitor = rawInputMonitor;
        _eventsRoot = eventsRoot;
        Directory.CreateDirectory(_eventsRoot);

        _rawInputMonitor.InputReceived += RawInputMonitorOnInputReceived;
        _eventAggregator.ActionReady += EventAggregatorOnActionReady;
    }

    public bool IsRecording { get; private set; }

    public async Task<SessionContext> StartAsync(TrackingMode mode, TrackingOptions options, TaskItem? givenTask)
    {
        lock (_sync)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("A recording session is already active.");
            }
        }

        _options = options;
        var sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var sessionFolder = Path.Combine(_eventsRoot, sessionId);

        _sessionContext = new SessionContext
        {
            SessionId = sessionId,
            SessionFolder = sessionFolder,
            Mode = mode,
            GivenTask = givenTask,
            Options = new TrackingOptions
            {
                WaitIntervalSeconds = options.WaitIntervalSeconds,
                CaptureFrameIntervalMs = options.CaptureFrameIntervalMs,
                PreferModernCapture = options.PreferModernCapture,
            },
            StartedAt = DateTimeOffset.Now,
        };

        _recorder = new SessionRecorder(sessionFolder);
        _captureService = CreateCaptureService(options);
        _sessionContext.CaptureBackend = _captureService.BackendName;

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
            _eventAggregator.FlushAll();

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

        capture?.Stop();
        capture?.Dispose();

        await recorder.CompleteAsync();
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

    private void EventAggregatorOnActionReady(ActionRecordDescriptor descriptor)
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

    private void OnMaintenanceTick(object? state)
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
        catch
        {
            screenshot?.Dispose();
            screenshot = null;
        }

        _recorder.Enqueue(descriptor, screenshot);
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
            catch
            {
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
            var bitmap = _captureService?.GetLatestFrameClone();
            if (bitmap is not null)
            {
                bitmap.Dispose();
                return;
            }

            await Task.Delay(100);
        }
    }
}
