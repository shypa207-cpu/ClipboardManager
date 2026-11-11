using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using ClipboardManager.Native;

namespace ClipboardManager.Services;

public static class PasteService
{
    public static bool SetText(string text) =>
        Retry(() => System.Windows.Clipboard.SetDataObject(text, true));

    public static bool SetImage(BitmapSource img) =>
        Retry(() => System.Windows.Clipboard.SetImage(img));

    private static bool Retry(Action action)
    {
        for (int i = 0; i < 10; i++)
        {
            try { action(); return true; }
            catch (ExternalException) { Thread.Sleep(30 * (i + 1)); }
            catch (Exception ex) { AppPaths.LogError("Clipboard write", ex); return false; }
        }
        return false;
    }

    /// <summary>Імітує Ctrl+V у вікні, яке зараз має фокус.</summary>
    public static void SendCtrlV()
    {
        // Відпустити модифікатори, які користувач може ще тримати (гаряча клавіша)
        ushort[] mods = { NativeMethods.VK_SHIFT, NativeMethods.VK_MENU, 0x5B, 0x5C };
        var list = new System.Collections.Generic.List<NativeMethods.INPUT>();
        foreach (var m in mods)
            if ((NativeMethods.GetAsyncKeyState(m) & 0x8000) != 0)
                list.Add(Key(m, true));

        list.Add(Key(NativeMethods.VK_CONTROL, false));
        list.Add(Key(NativeMethods.VK_V, false));
        list.Add(Key(NativeMethods.VK_V, true));
        list.Add(Key(NativeMethods.VK_CONTROL, true));

        var arr = list.ToArray();
        NativeMethods.SendInput((uint)arr.Length, arr, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT Key(ushort vk, bool up) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0
            }
        }
    };
}
