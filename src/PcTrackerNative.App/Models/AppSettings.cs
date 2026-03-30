namespace PcTrackerNative.App.Models;

public sealed class AppSettings
{
    public static string DefaultOutputRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "PC_Tracker_Output");

    public string OutputRoot { get; set; } = DefaultOutputRoot;

    public int WaitIntervalSeconds { get; set; } = 6;

    public int CaptureFrameIntervalMs { get; set; } = 180;

    public bool PreferModernCapture { get; set; } = true;
}
