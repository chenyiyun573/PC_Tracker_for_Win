using System.Text;
using PcTrackerNative.App.Models;

namespace PcTrackerNative.App.Services;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static string _outputRoot = AppSettings.DefaultOutputRoot;

    public static string CurrentLogPath
    {
        get
        {
            lock (Sync)
            {
                return BuildLogPath(_outputRoot, DateTime.Now);
            }
        }
    }

    public static string ConfigureOutputRoot(string? outputRoot)
    {
        lock (Sync)
        {
            _outputRoot = NormalizeOutputRoot(outputRoot);
            return BuildLogPath(_outputRoot, DateTime.Now);
        }
    }

    public static string PreviewLogPath(string? outputRoot)
    {
        lock (Sync)
        {
            return BuildLogPath(NormalizeOutputRoot(outputRoot), DateTime.Now);
        }
    }

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string context, Exception exception)
    {
        Write("ERROR", context, exception);
    }

    public static void Error(string message)
    {
        Write("ERROR", message, null);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            string logPath;
            lock (Sync)
            {
                _outputRoot = NormalizeOutputRoot(_outputRoot);
                logPath = BuildLogPath(_outputRoot, DateTime.Now);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? AppSettings.DefaultOutputRoot);

            var builder = new StringBuilder();
            builder.Append('[')
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append("] ")
                .Append(level)
                .Append(' ')
                .Append(message)
                .AppendLine();

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (Sync)
            {
                File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Do not throw from logger.
        }
    }

    private static string NormalizeOutputRoot(string? outputRoot)
    {
        var candidate = string.IsNullOrWhiteSpace(outputRoot)
            ? AppSettings.DefaultOutputRoot
            : outputRoot.Trim();

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch
        {
            return AppSettings.DefaultOutputRoot;
        }
    }

    private static string BuildLogPath(string outputRoot, DateTime now)
    {
        return Path.Combine(outputRoot, "logs", $"app-{now:yyyyMMdd}.log");
    }
}
