namespace PcTrackerNative.App.Models;

public enum InputEventKind
{
    Text,
    KeyChord,
    MouseClick,
    MouseWheel,
}

public enum MouseButtonKind
{
    None,
    Left,
    Right,
    Middle,
    X1,
    X2,
}

public sealed class InputEvent
{
    public InputEventKind Kind { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string? Text { get; init; }

    public string? KeyChord { get; init; }

    public MouseButtonKind MouseButton { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int WheelDelta { get; init; }

    public string WindowTitle { get; init; } = string.Empty;

    public string ProcessName { get; init; } = string.Empty;
}
