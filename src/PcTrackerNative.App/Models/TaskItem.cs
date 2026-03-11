namespace PcTrackerNative.App.Models;

public sealed class TaskItem
{
    public string Category { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public bool IsBad { get; set; }
}
