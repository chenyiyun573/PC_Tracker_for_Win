using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PcTrackerNative.App.Interop;

internal static class NativeMethods
{
    public const int WM_INPUT = 0x00FF;

    public const uint RID_INPUT = 0x10000003;

    public const uint RIM_TYPEMOUSE = 0;

    public const uint RIM_TYPEKEYBOARD = 1;

    public const int RIDEV_INPUTSINK = 0x00000100;

    public const int RI_KEY_BREAK = 0x0001;

    public const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;

    public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;

    public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;

    public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;

    public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;

    public const ushort RI_MOUSE_WHEEL = 0x0400;

    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    public const int VK_SHIFT = 0x10;

    public const int VK_CONTROL = 0x11;

    public const int VK_MENU = 0x12;

    public const int VK_LWIN = 0x5B;

    public const int VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public int dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWMOUSE
    {
        public ushort usFlags;
        public uint ulButtons;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;

        public ushort ButtonFlags => (ushort)(ulButtons & 0xFFFF);

        public ushort ButtonData => (ushort)((ulButtons >> 16) & 0xFFFF);
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct RAWINPUTDATA
    {
        [FieldOffset(0)]
        public RAWMOUSE mouse;

        [FieldOffset(0)]
        public RAWKEYBOARD keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWINPUTDATA data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    internal static bool TryExcludeFromCapture(IntPtr hwnd)
    {
        try
        {
            return SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        }
        catch
        {
            return false;
        }
    }

    internal static IntPtr GetPrimaryMonitorHandle()
    {
        return MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
    }

    internal static bool TryGetForegroundContext(int ownProcessId, out string windowTitle, out string processName)
    {
        windowTitle = string.Empty;
        processName = string.Empty;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == ownProcessId)
        {
            return false;
        }

        try
        {
            processName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            processName = $"pid-{pid}";
        }

        var length = Math.Max(256, GetWindowTextLength(hwnd) + 1);
        var builder = new StringBuilder(length);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        windowTitle = builder.ToString();
        return true;
    }
}
