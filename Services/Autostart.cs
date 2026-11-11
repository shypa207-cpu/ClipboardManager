using System;
using Microsoft.Win32;

namespace ClipboardManager.Services;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return k?.GetValue(AppPaths.AppName) is string;
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    k.SetValue(AppPaths.AppName, $"\"{exe}\" --background");
            }
            else
            {
                k.DeleteValue(AppPaths.AppName, false);
            }
        }
        catch (Exception ex) { AppPaths.LogError("Autostart", ex); }
    }
}
