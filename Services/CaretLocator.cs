using System;
using ClipboardManager.Native;

namespace ClipboardManager.Services;

public static class CaretLocator
{
    /// <summary>
    /// Повертає екранні координати (у фізичних пікселях) нижнього лівого кута каретки активного вікна.
    /// Якщо каретку отримати неможливо — позицію курсора миші.
    /// </summary>
    public static (int X, int Y, bool FromCaret) GetAnchor()
    {
        try
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                uint tid = NativeMethods.GetWindowThreadProcessId(fg, out _);
                var info = new NativeMethods.GUITHREADINFO
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
                };
                if (NativeMethods.GetGUIThreadInfo(tid, ref info) && info.hwndCaret != IntPtr.Zero)
                {
                    var r = info.rcCaret;
                    if (r.Right - r.Left > 0 || r.Bottom - r.Top > 0)
                    {
                        var pt = new NativeMethods.POINT { X = r.Left, Y = r.Bottom };
                        if (NativeMethods.ClientToScreen(info.hwndCaret, ref pt))
                            return (pt.X, pt.Y + 4, true);
                    }
                }
            }
        }
        catch (Exception ex) { AppPaths.LogError("Caret", ex); }

        NativeMethods.GetCursorPos(out var cur);
        return (cur.X, cur.Y, false);
    }
}
