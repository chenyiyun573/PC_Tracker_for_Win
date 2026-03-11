using System.Text;
using PcTrackerNative.App.Models;

namespace PcTrackerNative.App.Services.Tracking;

public sealed class EventAggregator
{
    private readonly int _typingFlushMs;
    private readonly int _scrollFlushMs;
    private readonly StringBuilder _typingBuffer = new();
    private DateTimeOffset? _typingLastTimestamp;
    private string _typingWindowTitle = string.Empty;
    private string _typingProcessName = string.Empty;

    private int _scrollDelta;
    private int _scrollX;
    private int _scrollY;
    private DateTimeOffset? _scrollLastTimestamp;
    private string _scrollWindowTitle = string.Empty;
    private string _scrollProcessName = string.Empty;

    public EventAggregator(int typingFlushMs = 800, int scrollFlushMs = 450)
    {
        _typingFlushMs = typingFlushMs;
        _scrollFlushMs = scrollFlushMs;
    }

    public event Action<ActionRecordDescriptor>? ActionReady;

    public void Handle(InputEvent inputEvent)
    {
        FlushExpired(inputEvent.Timestamp);

        switch (inputEvent.Kind)
        {
            case InputEventKind.Text:
                HandleText(inputEvent);
                break;
            case InputEventKind.KeyChord:
                FlushTyping();
                FlushScroll();
                Emit(new ActionRecordDescriptor
                {
                    Timestamp = inputEvent.Timestamp,
                    Type = "hotkey",
                    Description = $"Pressed {inputEvent.KeyChord}",
                    KeyChord = inputEvent.KeyChord,
                    WindowTitle = inputEvent.WindowTitle,
                    ProcessName = inputEvent.ProcessName,
                });
                break;
            case InputEventKind.MouseClick:
                FlushTyping();
                FlushScroll();
                Emit(new ActionRecordDescriptor
                {
                    Timestamp = inputEvent.Timestamp,
                    Type = "click",
                    Description = $"{inputEvent.MouseButton} click at ({inputEvent.X}, {inputEvent.Y})",
                    MouseButton = inputEvent.MouseButton.ToString(),
                    X = inputEvent.X,
                    Y = inputEvent.Y,
                    WindowTitle = inputEvent.WindowTitle,
                    ProcessName = inputEvent.ProcessName,
                });
                break;
            case InputEventKind.MouseWheel:
                FlushTyping();
                HandleScroll(inputEvent);
                break;
        }
    }

    public void Tick(DateTimeOffset now)
    {
        FlushExpired(now);
    }

    public void FlushAll()
    {
        FlushTyping();
        FlushScroll();
    }

    private void HandleText(InputEvent inputEvent)
    {
        if (string.IsNullOrWhiteSpace(inputEvent.Text))
        {
            return;
        }

        if (_typingBuffer.Length > 0 && (_typingWindowTitle != inputEvent.WindowTitle || _typingProcessName != inputEvent.ProcessName))
        {
            FlushTyping();
        }

        _typingBuffer.Append(inputEvent.Text);
        _typingLastTimestamp = inputEvent.Timestamp;
        _typingWindowTitle = inputEvent.WindowTitle;
        _typingProcessName = inputEvent.ProcessName;
    }

    private void HandleScroll(InputEvent inputEvent)
    {
        if (_scrollDelta != 0)
        {
            var timeGapMs = _scrollLastTimestamp.HasValue ? (inputEvent.Timestamp - _scrollLastTimestamp.Value).TotalMilliseconds : 0;
            var sameDirection = Math.Sign(_scrollDelta) == Math.Sign(inputEvent.WheelDelta);
            var sameWindow = _scrollWindowTitle == inputEvent.WindowTitle && _scrollProcessName == inputEvent.ProcessName;
            if (timeGapMs > _scrollFlushMs || !sameDirection || !sameWindow)
            {
                FlushScroll();
            }
        }

        _scrollDelta += inputEvent.WheelDelta;
        _scrollX = inputEvent.X;
        _scrollY = inputEvent.Y;
        _scrollLastTimestamp = inputEvent.Timestamp;
        _scrollWindowTitle = inputEvent.WindowTitle;
        _scrollProcessName = inputEvent.ProcessName;
    }

    private void FlushExpired(DateTimeOffset now)
    {
        if (_typingBuffer.Length > 0 && _typingLastTimestamp.HasValue && (now - _typingLastTimestamp.Value).TotalMilliseconds >= _typingFlushMs)
        {
            FlushTyping();
        }

        if (_scrollDelta != 0 && _scrollLastTimestamp.HasValue && (now - _scrollLastTimestamp.Value).TotalMilliseconds >= _scrollFlushMs)
        {
            FlushScroll();
        }
    }

    private void FlushTyping()
    {
        if (_typingBuffer.Length == 0)
        {
            return;
        }

        var text = _typingBuffer.ToString();
        Emit(new ActionRecordDescriptor
        {
            Timestamp = _typingLastTimestamp ?? DateTimeOffset.Now,
            Type = "type",
            Description = $"Typed: \"{DisplayText(text)}\"",
            TextValue = text,
            WindowTitle = _typingWindowTitle,
            ProcessName = _typingProcessName,
        });

        _typingBuffer.Clear();
        _typingLastTimestamp = null;
        _typingWindowTitle = string.Empty;
        _typingProcessName = string.Empty;
    }

    private void FlushScroll()
    {
        if (_scrollDelta == 0)
        {
            return;
        }

        var notches = Math.Abs(_scrollDelta) / 120.0;
        var direction = _scrollDelta > 0 ? "up" : "down";
        Emit(new ActionRecordDescriptor
        {
            Timestamp = _scrollLastTimestamp ?? DateTimeOffset.Now,
            Type = "scroll",
            Description = $"Scrolled {direction} ({notches:0.##} notch(es))",
            WheelDelta = _scrollDelta,
            X = _scrollX,
            Y = _scrollY,
            WindowTitle = _scrollWindowTitle,
            ProcessName = _scrollProcessName,
        });

        _scrollDelta = 0;
        _scrollX = 0;
        _scrollY = 0;
        _scrollLastTimestamp = null;
        _scrollWindowTitle = string.Empty;
        _scrollProcessName = string.Empty;
    }

    private void Emit(ActionRecordDescriptor descriptor)
    {
        ActionReady?.Invoke(descriptor);
    }

    private static string DisplayText(string text)
    {
        return text.Replace("\r", string.Empty).Replace("\n", "↵").Replace("\t", "⇥");
    }
}
