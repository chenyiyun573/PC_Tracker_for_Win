using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PcTrackerNative.App.Interop;
using PcTrackerNative.App.Models;
using PcTrackerNative.App.Services;
using PcTrackerNative.App.Services.Input;
using PcTrackerNative.App.Services.Tracking;
using MessageBox = System.Windows.MessageBox;
namespace PcTrackerNative.App;

public partial class MainWindow : Window
{
    private readonly string _eventsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PcTrackerNative",
        "events");

    private readonly TaskStore _taskStore;
    private RawInputMonitor? _rawInputMonitor;
    private TrackerController? _trackerController;
    private DispatcherTimer? _uiTimer;
    private IntPtr _hwnd;
    private SessionContext? _activeSession;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(_eventsRoot);
        _taskStore = new TaskStore(Path.Combine(AppContext.BaseDirectory, "tasks.json"));
        FooterTextBlock.Text = $"Output folder: {_eventsRoot}";
        WaitIntervalPreviewTextBlock.Text = "6 s";
        SessionTimerTextBlock.Text = "00:00:00";

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        _ = NativeMethods.TryExcludeFromCapture(_hwnd);

        _rawInputMonitor = new RawInputMonitor(_hwnd, Process.GetCurrentProcess().Id);
        _trackerController = new TrackerController(_rawInputMonitor, _eventsRoot);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshTaskUi();
        UpdateRecordingUi(false, TrackingMode.GivenTask);
        StatusTextBlock.Text = "Ready.";
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _rawInputMonitor?.ProcessWindowMessage(msg, lParam);
        return IntPtr.Zero;
    }

    private async void StartGivenTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var task = _taskStore.CurrentTask;
        if (task is null)
        {
            MessageBox.Show(this, "There are no pending tasks left in tasks.json.", "PC Tracker Native", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await StartRecordingAsync(TrackingMode.GivenTask, task);
    }

    private async void StartFreeTaskButton_Click(object sender, RoutedEventArgs e)
    {
        await StartRecordingAsync(TrackingMode.FreeTask, null);
    }

    private async void StartNonTaskButton_Click(object sender, RoutedEventArgs e)
    {
        await StartRecordingAsync(TrackingMode.NonTask, null);
    }

    private async void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        await StopAndPromptAsync(_activeSession?.Mode == TrackingMode.GivenTask ? SessionOutcome.Saved : SessionOutcome.Saved);
    }

    private async void FailButton_Click(object sender, RoutedEventArgs e)
    {
        await StopAndPromptAsync(SessionOutcome.Failed);
    }

    private async void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_trackerController?.IsRecording != true)
        {
            return;
        }

        var result = MessageBox.Show(this, "Discard the current recording?", "PC Tracker Native", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await StopAndPromptAsync(SessionOutcome.Discarded);
    }

    private void PreviousTaskButton_Click(object sender, RoutedEventArgs e)
    {
        _taskStore.MovePrevious();
        RefreshTaskUi();
    }

    private void NextTaskButton_Click(object sender, RoutedEventArgs e)
    {
        _taskStore.MoveNext();
        RefreshTaskUi();
    }

    private void BadTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var currentTask = _taskStore.CurrentTask;
        if (currentTask is null)
        {
            return;
        }

        var result = MessageBox.Show(this, "Mark the current task as bad and skip it?", "PC Tracker Native", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _taskStore.MarkCurrentBad();
        RefreshTaskUi();
    }

    private void OpenOutputButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _eventsRoot,
            UseShellExecute = true,
        });
    }

    private async Task StartRecordingAsync(TrackingMode mode, TaskItem? task)
    {
        if (_trackerController is null)
        {
            return;
        }

        try
        {
            var options = BuildTrackingOptions();
            _activeSession = await _trackerController.StartAsync(mode, options, task);
            CaptureBackendTextBlock.Text = _activeSession.CaptureBackend;
            WaitIntervalPreviewTextBlock.Text = $"{options.WaitIntervalSeconds} s";
            StatusTextBlock.Text = $"Recording {mode}...";
            UpdateRecordingUi(true, mode);
            StartUiTimer();
            WindowState = WindowState.Minimized;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "PC Tracker Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopAndPromptAsync(SessionOutcome outcome)
    {
        if (_trackerController is null || _activeSession is null)
        {
            return;
        }

        WindowState = WindowState.Normal;
        var draft = await _trackerController.StopAsync(outcome);
        StopUiTimer();
        UpdateRecordingUi(false, draft.Context.Mode);
        SessionTimerTextBlock.Text = "00:00:00";

        if (outcome == SessionOutcome.Discarded)
        {
            await _trackerController.DiscardDraftAsync(draft);
            _activeSession = null;
            StatusTextBlock.Text = "Recording discarded.";
            RefreshTaskUi();
            return;
        }

        var defaultDescription = draft.Context.Mode switch
        {
            TrackingMode.GivenTask => draft.Context.GivenTask?.Description ?? "Given task session",
            TrackingMode.FreeTask => "Free task session",
            _ => "Non-task session",
        };

        var dialog = new SaveSessionWindow(draft.Context.Mode, outcome, defaultDescription)
        {
            Owner = this,
        };

        var dialogResult = dialog.ShowDialog();
        if (dialogResult == true)
        {
            var saveInfo = dialog.BuildSaveInfo(outcome);
            await _trackerController.CommitDraftAsync(draft, saveInfo);
            if (draft.Context.Mode == TrackingMode.GivenTask && outcome == SessionOutcome.Saved)
            {
                _taskStore.MarkCurrentDone();
            }

            StatusTextBlock.Text = $"Saved session: {draft.Context.SessionId}";
        }
        else
        {
            await _trackerController.DiscardDraftAsync(draft);
            StatusTextBlock.Text = "Recording discarded.";
        }

        _activeSession = null;
        RefreshTaskUi();
    }

    private TrackingOptions BuildTrackingOptions()
    {
        var waitSeconds = ParseInt(WaitIntervalTextBox.Text, 6, 1, 3600);
        var frameIntervalMs = ParseInt(CaptureFrameIntervalTextBox.Text, 180, 33, 2000);
        PreferModernCaptureCheckBox.IsChecked ??= true;

        return new TrackingOptions
        {
            WaitIntervalSeconds = waitSeconds,
            CaptureFrameIntervalMs = frameIntervalMs,
            PreferModernCapture = PreferModernCaptureCheckBox.IsChecked == true,
        };
    }

    private void RefreshTaskUi()
    {
        var currentTask = _taskStore.CurrentTask;
        GivenTaskCounterTextBlock.Text = $"Pending tasks: {_taskStore.PendingCount}    Completed: {_taskStore.CompletedCount}";
        if (currentTask is null)
        {
            GivenTaskCategoryTextBlock.Text = "No pending task";
            GivenTaskDescriptionTextBox.Text = "All tasks are completed or marked bad.";
            StartGivenTaskButton.IsEnabled = false;
            PreviousTaskButton.IsEnabled = false;
            NextTaskButton.IsEnabled = false;
            BadTaskButton.IsEnabled = false;
            return;
        }

        GivenTaskCategoryTextBlock.Text = currentTask.Category;
        GivenTaskDescriptionTextBox.Text = currentTask.Description;
        StartGivenTaskButton.IsEnabled = true;
        PreviousTaskButton.IsEnabled = true;
        NextTaskButton.IsEnabled = true;
        BadTaskButton.IsEnabled = true;
    }

    private void UpdateRecordingUi(bool isRecording, TrackingMode mode)
    {
        StartGivenTaskButton.IsEnabled = !isRecording && _taskStore.CurrentTask is not null;
        StartFreeTaskButton.IsEnabled = !isRecording;
        StartNonTaskButton.IsEnabled = !isRecording;
        PreviousTaskButton.IsEnabled = !isRecording && _taskStore.CurrentTask is not null;
        NextTaskButton.IsEnabled = !isRecording && _taskStore.CurrentTask is not null;
        BadTaskButton.IsEnabled = !isRecording && _taskStore.CurrentTask is not null;
        ModeTabControl.IsEnabled = !isRecording;

        FinishButton.Visibility = isRecording ? Visibility.Visible : Visibility.Collapsed;
        DiscardButton.Visibility = isRecording ? Visibility.Visible : Visibility.Collapsed;
        FailButton.Visibility = isRecording && mode == TrackingMode.GivenTask ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Content = mode == TrackingMode.GivenTask ? "Finish" : "Stop / Save";
    }

    private void StartUiTimer()
    {
        _uiTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick -= UiTimerOnTick;
        _uiTimer.Tick += UiTimerOnTick;
        _uiTimer.Start();
    }

    private void StopUiTimer()
    {
        _uiTimer?.Stop();
    }

    private void UiTimerOnTick(object? sender, EventArgs e)
    {
        if (_activeSession is null)
        {
            SessionTimerTextBlock.Text = "00:00:00";
            return;
        }

        var elapsed = DateTimeOffset.Now - _activeSession.StartedAt;
        SessionTimerTextBlock.Text = elapsed.ToString(@"hh\:mm\:ss");
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_trackerController?.IsRecording == true)
        {
            MessageBox.Show(this, "Please finish, fail, or discard the active recording before closing the app.", "PC Tracker Native", MessageBoxButton.OK, MessageBoxImage.Information);
            e.Cancel = true;
            return;
        }

        _trackerController?.Dispose();
        _rawInputMonitor?.Dispose();
    }

    private static int ParseInt(string? text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, out var value))
        {
            return fallback;
        }

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
