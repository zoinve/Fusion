using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace YPM.UI.Services;

public sealed class TrayIconService : IDisposable
{
    private const int WmTrayIcon = 0x8001;
    private const int WmCommand = 0x0111;
    private const int WmDestroy = 0x0002;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;

    private const uint WmUser = 0x0400;
    private const uint MfString = 0x00000000;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmRightButton = 0x0002;

    private const uint CmdOpen = 1001;
    private const uint CmdExit = 1002;

    private static readonly ConcurrentDictionary<IntPtr, TrayIconService> Instances = new();
    private static readonly WindowProcDelegate WindowProc = StaticWndProc;

    private readonly Action _openAction;
    private readonly Action _exitAction;
    private NOTIFYICONDATA _notifyIconData;
    private readonly string _windowClassName = $"FusionTrayIconWindow_{Guid.NewGuid():N}";
    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private bool _disposed;

    public TrayIconService(Action openAction, Action exitAction, string tooltip)
    {
        _openAction = openAction;
        _exitAction = exitAction;

        RegisterWindowClass();
        CreateMessageWindow();
        _iconHandle = LoadAppIcon();

        _notifyIconData = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = _iconHandle,
            szTip = tooltip
        };

        Shell_NotifyIcon(NimAdd, ref _notifyIconData);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var data = _notifyIconData;
        Shell_NotifyIcon(NimDelete, ref data);

        if (_windowHandle != IntPtr.Zero)
        {
            Instances.TryRemove(_windowHandle, out _);
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        UnregisterClass(_windowClassName, IntPtr.Zero);
    }

    private void RegisterWindowClass()
    {
        var wc = new WNDCLASS
        {
            lpfnWndProc = WindowProc,
            lpszClassName = _windowClassName
        };

        RegisterClass(ref wc);
    }

    private void CreateMessageWindow()
    {
        _windowHandle = CreateWindowEx(
            0,
            _windowClassName,
            _windowClassName,
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        Instances[_windowHandle] = this;
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (Instances.TryGetValue(hWnd, out var instance))
        {
            return instance.ProcessWindowMessage(msg, wParam, lParam);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr ProcessWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmTrayIcon)
        {
            switch ((uint)lParam.ToInt64())
            {
                case WmLButtonUp:
                    _openAction();
                    return IntPtr.Zero;
                case WmRButtonUp:
                    ShowContextMenu();
                    return IntPtr.Zero;
            }
        }

        if (msg == WmCommand)
        {
            switch ((uint)wParam.ToInt64() & 0xFFFF)
            {
                case CmdOpen:
                    _openAction();
                    return IntPtr.Zero;
                case CmdExit:
                    _exitAction();
                    return IntPtr.Zero;
            }
        }

        if (msg == WmDestroy)
        {
            PostQuitMessage(0);
        }

        return DefWindowProc(_windowHandle, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, CmdOpen, "打开 Fusion");
        AppendMenu(menu, MfString, CmdExit, "退出");

        GetCursorPos(out var point);
        SetForegroundWindow(_windowHandle);
        TrackPopupMenu(menu, TpmLeftAlign | TpmBottomAlign | TpmRightButton, point.X, point.Y, 0, _windowHandle, IntPtr.Zero);
        DestroyMenu(menu);
    }

    private static IntPtr LoadAppIcon()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return IntPtr.Zero;
        }

        ExtractIconEx(processPath, 0, out _, out var smallIcon, 1);
        return smallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public WindowProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr WindowProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        int nReserved,
        IntPtr hWnd,
        IntPtr prcRect);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
