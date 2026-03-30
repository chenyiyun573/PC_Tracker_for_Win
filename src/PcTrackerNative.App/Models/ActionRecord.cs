namespace PcTrackerNative.App.Models;

public sealed class ActionRecord
{
    public int Step { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int? X { get; set; }

    public int? Y { get; set; }

    public int? WheelDelta { get; set; }

    public double? WaitSeconds { get; set; }

    public string? TextValue { get; set; }

    public string? KeyChord { get; set; }

    public string? MouseButton { get; set; }

    public string WindowTitle { get; set; } = string.Empty;

    public string ProcessName { get; set; } = string.Empty;

    public string? ScreenshotPath { get; set; }
}

public sealed class ActionRecordDescriptor
{
    public DateTimeOffset Timestamp { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int? X { get; set; }

    public int? Y { get; set; }

    public int? WheelDelta { get; set; }

    public double? WaitSeconds { get; set; }

    public string? TextValue { get; set; }

    public string? KeyChord { get; set; }

    public string? MouseButton { get; set; }

    public string WindowTitle { get; set; } = string.Empty;

    public string ProcessName { get; set; } = string.Empty;
}
