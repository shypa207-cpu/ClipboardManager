using System;
using System.Windows.Interop;

namespace ClipboardManager.Services;

/// <summary>Невидиме message-only вікно: отримує WM_CLIPBOARDUPDATE та WM_HOTKEY.</summary>
public sealed class HiddenWindow : IDisposable
{
    private readonly HwndSource _source;

    public HiddenWindow()
    {
        var p = new HwndSourceParameters("ClipboardManagerSink")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(p);
    }

    public IntPtr Handle => _source.Handle;
    public void AddHook(HwndSourceHook hook) => _source.AddHook(hook);
    public void RemoveHook(HwndSourceHook hook) => _source.RemoveHook(hook);
    public void Dispose() => _source.Dispose();
}
