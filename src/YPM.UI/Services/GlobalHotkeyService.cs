using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using WinRT.Interop;
using Microsoft.UI.Xaml;

namespace YPM.UI.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint WM_HOTKEY = 0x0312;

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _dispatcher;
    private readonly WndProc _subclassProc;
    private readonly IntPtr _subclassId;
    private bool _registered;
    private bool _disposed;

    public event Action? PlayPause;
    public event Action? NextTrack;
    public event Action? PreviousTrack;
    public event Action? VolumeUp;
    public event Action? VolumeDown;

    public GlobalHotkeyService(Window window, DispatcherQueue dispatcher)
    {
        _hwnd = WindowNative.GetWindowHandle(window);
        _dispatcher = dispatcher;
        _subclassProc = SubclassProc;
        _subclassId = new IntPtr(1);
        SetWindowSubclass(_hwnd, _subclassProc, (UIntPtr)_subclassId, IntPtr.Zero);
    }

    public void RegisterAll()
    {
        if (_registered) return;
        _registered = true;

        RegisterHotKey(_hwnd, 1, MOD_CONTROL | MOD_SHIFT, 0x50); // P
        RegisterHotKey(_hwnd, 2, MOD_CONTROL | MOD_SHIFT, 0x25); // Left
        RegisterHotKey(_hwnd, 3, MOD_CONTROL | MOD_SHIFT, 0x27); // Right
        RegisterHotKey(_hwnd, 4, MOD_CONTROL | MOD_SHIFT, 0x26); // Up
        RegisterHotKey(_hwnd, 5, MOD_CONTROL | MOD_SHIFT, 0x28); // Down
    }

    public void UnregisterAll()
    {
        if (!_registered) return;
        _registered = false;

        for (var i = 1; i <= 5; i++)
        {
            UnregisterHotKey(_hwnd, i);
        }
    }

    private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, UIntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_HOTKEY)
        {
            var id = (int)wParam.ToUInt32();
            _dispatcher.TryEnqueue(() =>
            {
                switch (id)
                {
                    case 1: PlayPause?.Invoke(); break;
                    case 2: PreviousTrack?.Invoke(); break;
                    case 3: NextTrack?.Invoke(); break;
                    case 4: VolumeUp?.Invoke(); break;
                    case 5: VolumeDown?.Invoke(); break;
                }
            });
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        RemoveWindowSubclass(_hwnd, _subclassProc, (UIntPtr)_subclassId);
    }

    // ── P/Invoke ────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, WndProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, WndProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, UIntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProc(IntPtr hWnd, uint uMsg, UIntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);
}
