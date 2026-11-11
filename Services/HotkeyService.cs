using System;
using ClipboardManager.Native;

namespace ClipboardManager.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xC11B;
    private readonly HiddenWindow _window;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyService(HiddenWindow window)
    {
        _window = window;
        _window.AddHook(WndProc);
    }

    public bool Register(uint modifiers, uint vk)
    {
        Unregister();
        _registered = NativeMethods.RegisterHotKey(_window.Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHook(WndProc);
    }
}
