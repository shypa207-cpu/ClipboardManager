using System;
using System.IO;
using System.Text.Json;

namespace ClipboardManager.Services;

public sealed class Settings
{
    public int HistoryLimit { get; set; } = 50;
    public bool AutoStart { get; set; }
    // Модифікатори: Ctrl=2, Shift=4, Alt=1, Win=8 (як у WinAPI). Клавіша — віртуальний код (V = 0x56)
    public uint HotkeyModifiers { get; set; } = 0x0002 | 0x0004;
    public uint HotkeyKey { get; set; } = 0x56;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings();
        }
        catch (Exception ex) { AppPaths.LogError("Settings load", ex); }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsFile,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { AppPaths.LogError("Settings save", ex); }
    }
}
