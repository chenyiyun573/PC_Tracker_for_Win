using System.Windows;
using System.Windows.Threading;
using PcTrackerNative.App.Services;

namespace PcTrackerNative.App;

public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled UI exception.", e.Exception);
        System.Windows.MessageBox.Show(
            $"An unexpected error occurred. A log file was written to:\n{AppLogger.CurrentLogPath}",
            "PC Tracker Native",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppLogger.Error("Unhandled AppDomain exception.", exception);
        }
        else
        {
            AppLogger.Error($"Unhandled AppDomain exception: {e.ExceptionObject}");
        }
    }

    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
