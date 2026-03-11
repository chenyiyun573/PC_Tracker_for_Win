using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using PcTrackerNative.App.Interop;
using PcTrackerNative.App.Models;

namespace PcTrackerNative.App.Services.Input;

public sealed class RawInputMonitor : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly int _ownProcessId;
    private readonly HashSet<int> _pressedKeys = new();
    private bool _started;

    public RawInputMonitor(IntPtr hwnd, int ownProcessId)
    {
        _hwnd = hwnd;
        _ownProcessId = ownProcessId;
    }

    public event EventHandler<InputEvent>? InputReceived;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        var devices = new[]
        {
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x02,
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = _hwnd,
            },
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = _hwnd,
            },
        };

        if (!NativeMethods.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
        {
            throw new InvalidOperationException("RegisterRawInputDevices failed.");
        }

        _started = true;
    }

    public void Stop()
    {
        _started = false;
        _pressedKeys.Clear();
    }

    public void Dispose()
    {
        Stop();
    }

    public void ProcessWindowMessage(int msg, IntPtr lParam)
    {
        if (!_started || msg != NativeMethods.WM_INPUT)
        {
            return;
        }

        uint dataSize = 0;
        var headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        _ = NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, IntPtr.Zero, ref dataSize, headerSize);
        if (dataSize == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)dataSize);
        try
        {
            if (NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, buffer, ref dataSize, headerSize) != dataSize)
            {
                return;
            }

            var rawInput = Marshal.PtrToStructure<NativeMethods.RAWINPUT>(buffer);
            switch (rawInput.header.dwType)
            {
                case NativeMethods.RIM_TYPEKEYBOARD:
                    HandleKeyboard(rawInput.data.keyboard);
                    break;
                case NativeMethods.RIM_TYPEMOUSE:
                    HandleMouse(rawInput.data.mouse);
                    break;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void HandleKeyboard(NativeMethods.RAWKEYBOARD keyboard)
    {
        var virtualKey = keyboard.VKey;
        if (virtualKey == 0 || virtualKey == 255)
        {
            return;
        }

        var isBreak = (keyboard.Flags & NativeMethods.RI_KEY_BREAK) != 0;
        var normalizedVirtualKey = NormalizeVirtualKey(virtualKey);
        if (isBreak)
        {
            _pressedKeys.Remove(normalizedVirtualKey);
            return;
        }

        if (_pressedKeys.Contains(normalizedVirtualKey))
        {
            return;
        }

        _pressedKeys.Add(normalizedVirtualKey);
        if (IsModifierKey(normalizedVirtualKey))
        {
            return;
        }

        if (!NativeMethods.TryGetForegroundContext(_ownProcessId, out var title, out var processName))
        {
            return;
        }

        var ctrlPressed = IsPressed(NativeMethods.VK_CONTROL);
        var altPressed = IsPressed(NativeMethods.VK_MENU);
        var shiftPressed = IsPressed(NativeMethods.VK_SHIFT);
        var winPressed = IsPressed(NativeMethods.VK_LWIN) || IsPressed(NativeMethods.VK_RWIN);

        if (!ctrlPressed && !altPressed && !winPressed)
        {
            var text = TryTranslateToText(normalizedVirtualKey, keyboard.MakeCode);
            if (!string.IsNullOrWhiteSpace(text))
            {
                InputReceived?.Invoke(this, new InputEvent
                {
                    Kind = InputEventKind.Text,
                    Timestamp = DateTimeOffset.Now,
                    Text = text,
                    WindowTitle = title,
                    ProcessName = processName,
                });
                return;
            }
        }

        var chord = BuildChord(normalizedVirtualKey, ctrlPressed, altPressed, shiftPressed, winPressed);
        InputReceived?.Invoke(this, new InputEvent
        {
            Kind = InputEventKind.KeyChord,
            Timestamp = DateTimeOffset.Now,
            KeyChord = chord,
            WindowTitle = title,
            ProcessName = processName,
        });
    }

    private void HandleMouse(NativeMethods.RAWMOUSE mouse)
    {
        if (!NativeMethods.TryGetForegroundContext(_ownProcessId, out var title, out var processName))
        {
            return;
        }

        _ = NativeMethods.GetCursorPos(out var point);
        var buttonFlags = mouse.ButtonFlags;

        if ((buttonFlags & NativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
        {
            EmitMouseClick(MouseButtonKind.Left, point.X, point.Y, title, processName);
            return;
        }

        if ((buttonFlags & NativeMethods.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
        {
            EmitMouseClick(MouseButtonKind.Right, point.X, point.Y, title, processName);
            return;
        }

        if ((buttonFlags & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
        {
            EmitMouseClick(MouseButtonKind.Middle, point.X, point.Y, title, processName);
            return;
        }

        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_4_DOWN) != 0)
        {
            EmitMouseClick(MouseButtonKind.X1, point.X, point.Y, title, processName);
            return;
        }

        if ((buttonFlags & NativeMethods.RI_MOUSE_BUTTON_5_DOWN) != 0)
        {
            EmitMouseClick(MouseButtonKind.X2, point.X, point.Y, title, processName);
            return;
        }

        if ((buttonFlags & NativeMethods.RI_MOUSE_WHEEL) != 0)
        {
            InputReceived?.Invoke(this, new InputEvent
            {
                Kind = InputEventKind.MouseWheel,
                Timestamp = DateTimeOffset.Now,
                WheelDelta = unchecked((short)mouse.ButtonData),
                X = point.X,
                Y = point.Y,
                WindowTitle = title,
                ProcessName = processName,
            });
        }
    }

    private void EmitMouseClick(MouseButtonKind button, int x, int y, string title, string processName)
    {
        InputReceived?.Invoke(this, new InputEvent
        {
            Kind = InputEventKind.MouseClick,
            Timestamp = DateTimeOffset.Now,
            MouseButton = button,
            X = x,
            Y = y,
            WindowTitle = title,
            ProcessName = processName,
        });
    }

    private string? TryTranslateToText(int virtualKey, ushort makeCode)
    {
        var keyboardState = new byte[256];
        if (!NativeMethods.GetKeyboardState(keyboardState))
        {
            return null;
        }

        var buffer = new StringBuilder(8);
        var result = NativeMethods.ToUnicodeEx((uint)virtualKey, makeCode, keyboardState, buffer, buffer.Capacity, 0, NativeMethods.GetKeyboardLayout(0));
        if (result <= 0)
        {
            return null;
        }

        var text = buffer.ToString(0, result);
        if (text.Any(char.IsControl))
        {
            return null;
        }

        return text;
    }

    private bool IsPressed(int virtualKey)
    {
        return _pressedKeys.Contains(virtualKey);
    }

    private static bool IsModifierKey(int virtualKey)
    {
        return virtualKey is NativeMethods.VK_SHIFT or NativeMethods.VK_CONTROL or NativeMethods.VK_MENU or NativeMethods.VK_LWIN or NativeMethods.VK_RWIN;
    }

    private static int NormalizeVirtualKey(int virtualKey)
    {
        return virtualKey switch
        {
            0xA2 => NativeMethods.VK_CONTROL,
            0xA3 => NativeMethods.VK_CONTROL,
            0xA4 => NativeMethods.VK_MENU,
            0xA5 => NativeMethods.VK_MENU,
            0xA0 => NativeMethods.VK_SHIFT,
            0xA1 => NativeMethods.VK_SHIFT,
            _ => virtualKey,
        };
    }

    private static string BuildChord(int virtualKey, bool ctrlPressed, bool altPressed, bool shiftPressed, bool winPressed)
    {
        var parts = new List<string>();
        if (ctrlPressed)
        {
            parts.Add("Ctrl");
        }

        if (altPressed)
        {
            parts.Add("Alt");
        }

        if (shiftPressed)
        {
            parts.Add("Shift");
        }

        if (winPressed)
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyName(virtualKey));
        return string.Join("+", parts.Distinct());
    }

    private static string GetKeyName(int virtualKey)
    {
        if (virtualKey >= 0x30 && virtualKey <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey >= 0x41 && virtualKey <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return key switch
        {
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Tab => "Tab",
            Key.Space => "Space",
            Key.Delete => "Delete",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Home => "Home",
            Key.End => "End",
            Key.Insert => "Insert",
            Key.System => "Alt",
            _ when key != Key.None => key.ToString(),
            _ => $"VK_{virtualKey:X2}",
        };
    }
}
