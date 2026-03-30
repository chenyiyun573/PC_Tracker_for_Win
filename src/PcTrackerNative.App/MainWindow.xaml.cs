using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using PcTrackerNative.App.Interop;
using PcTrackerNative.App.Models;
using PcTrackerNative.App.Services;
using PcTrackerNative.App.Services.Input;
using PcTrackerNative.App.Services.Tracking;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;

namespace PcTrackerNative.App;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private RawInputMonitor? _rawInputMonitor;
    private TrackerController? _trackerController;
    private DispatcherTimer? _uiTimer;
    private IntPtr _hwnd;
    private SessionContext? _activeSession;

    public MainWindow()
    {
        InitializeComponent();

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            _ = NativeMethods.TryExcludeFromCapture(_hwnd);

            _rawInputMonitor = new RawInputMonitor(_hwnd, Process.GetCurrentProcess().Id);
            _trackerController = new TrackerController(_rawInputMonitor);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Main window initialization failed.", ex);
            MessageBox.Show(this, ex.Message, "PC Tracker Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySettingsToUi();
        UpdateRecordingUi(false);
        CaptureBackendTextBlock.Text = "Idle";
        SessionTimerTextBlock.Text = "00:00:00";
        StatusTextBlock.Text = "Ready.";
        RefreshLogDestinationPreview();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            _rawInputMonitor?.ProcessWindowMessage(msg, lParam);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Window message handling failed.", ex);
        }

        return IntPtr.Zero;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        await StartRecordingAsync();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopRecordingAsync();
    }

    private void OutputFolderTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _trackerController?.IsRecording == true)
        {
            return;
        }

        _settings.OutputRoot = GetOutputRoot();
        RefreshLogDestinationPreview();
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (_trackerController?.IsRecording == true)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the output folder for PC Tracker sessions.",
            UseDescriptionForTitle = true,
            SelectedPath = GetOutputRoot(),
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            OutputFolderTextBox.Text = dialog.SelectedPath;
            _settings.OutputRoot = dialog.SelectedPath;
            RefreshLogDestinationPreview();
        }
    }

    private void OpenOutputButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var outputRoot = GetOutputRoot();
            Directory.CreateDirectory(outputRoot);
            AppLogger.ConfigureOutputRoot(outputRoot);
            RefreshLogDestinationPreview();
            Process.Start(new ProcessStartInfo
            {
                FileName = outputRoot,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to open the output folder.", ex);
            MessageBox.Show(this, ex.Message, "PC Tracker Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StartRecordingAsync()
    {
        if (_trackerController is null)
        {
            return;
        }

        try
        {
            SyncSettingsFromUi();
            var outputRoot = GetOutputRoot();
            Directory.CreateDirectory(outputRoot);
            AppLogger.ConfigureOutputRoot(outputRoot);

            var options = BuildTrackingOptions();
            _activeSession = await _trackerController.StartAsync(options, outputRoot);
            CaptureBackendTextBlock.Text = _activeSession.CaptureBackend;
            StatusTextBlock.Text = $"Recording started. Session: {_activeSession.SessionId}";
            UpdateRecordingUi(true);
            StartUiTimer();
            RefreshLogDestinationPreview();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to start recording.", ex);
            MessageBox.Show(this, ex.Message, "PC Tracker Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopRecordingAsync()
    {
        if (_trackerController is null || _activeSession is null)
        {
            return;
        }

        try
        {
            var draft = await _trackerController.StopAsync(SessionOutcome.Saved);
            var saveInfo = new SessionSaveInfo
            {
                Outcome = SessionOutcome.Saved,
                FinalDescription = string.Empty,
                Difficulty = TaskDifficulty.Medium,
                Notes = string.Empty,
            };

            await _trackerController.CommitDraftAsync(draft, saveInfo);
            _activeSession = null;
            StopUiTimer();
            SessionTimerTextBlock.Text = "00:00:00";
            UpdateRecordingUi(false);
            StatusTextBlock.Text = $"Saved session: {draft.Context.SessionId}";
            CaptureBackendTextBlock.Text = "Idle";
            RefreshLogDestinationPreview();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to stop recording.", ex);
            MessageBox.Show(this, ex.Message, "PC Tracker Native", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private TrackingOptions BuildTrackingOptions()
    {
        return new TrackingOptions
        {
            WaitIntervalSeconds = ParseInt(WaitIntervalTextBox.Text, _settings.WaitIntervalSeconds, 1, 3600),
            CaptureFrameIntervalMs = ParseInt(CaptureFrameIntervalTextBox.Text, _settings.CaptureFrameIntervalMs, 33, 2000),
            PreferModernCapture = PreferModernCaptureCheckBox.IsChecked != false,
        };
    }

    private string GetOutputRoot()
    {
        var path = OutputFolderTextBox.Text?.Trim();
        return string.IsNullOrWhiteSpace(path) ? _settings.OutputRoot : path;
    }

    private void ApplySettingsToUi()
    {
        OutputFolderTextBox.Text = _settings.OutputRoot;
        WaitIntervalTextBox.Text = _settings.WaitIntervalSeconds.ToString();
        CaptureFrameIntervalTextBox.Text = _settings.CaptureFrameIntervalMs.ToString();
        PreferModernCaptureCheckBox.IsChecked = _settings.PreferModernCapture;
    }

    private void SyncSettingsFromUi()
    {
        _settings = new AppSettings
        {
            OutputRoot = GetOutputRoot(),
            WaitIntervalSeconds = ParseInt(WaitIntervalTextBox.Text, _settings.WaitIntervalSeconds, 1, 3600),
            CaptureFrameIntervalMs = ParseInt(CaptureFrameIntervalTextBox.Text, _settings.CaptureFrameIntervalMs, 33, 2000),
            PreferModernCapture = PreferModernCaptureCheckBox.IsChecked != false,
        };

        RefreshLogDestinationPreview();
    }

    private void RefreshLogDestinationPreview()
    {
        var previewPath = AppLogger.ConfigureOutputRoot(GetOutputRoot());
        FooterTextBlock.Text = $"Logs: {previewPath}";
    }

    private void UpdateRecordingUi(bool isRecording)
    {
        StartButton.IsEnabled = !isRecording;
        StopButton.IsEnabled = isRecording;
        OutputFolderTextBox.IsEnabled = !isRecording;
        BrowseOutputButton.IsEnabled = !isRecording;
        WaitIntervalTextBox.IsEnabled = !isRecording;
        CaptureFrameIntervalTextBox.IsEnabled = !isRecording;
        PreferModernCaptureCheckBox.IsEnabled = !isRecording;
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
            MessageBox.Show(this, "Please stop the active recording before closing the app.", "PC Tracker Native", MessageBoxButton.OK, MessageBoxImage.Information);
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
