using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EkipVisual.Services;

// Raccourci global Ctrl+Alt+E pour activer/désactiver l'effet sans devoir alt-tab vers l'app.
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xEC01;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint VK_E = 0x45;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly bool _registered;

    public event Action? Pressed;

    public HotkeyService(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("La fenêtre n'a pas encore de handle natif.");
        _source.AddHook(WndProc);
        _registered = RegisterHotKey(handle, HotkeyId, MOD_CONTROL | MOD_ALT, VK_E);
    }

    public bool IsRegistered => _registered;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
    }
}
