using System;
using System.IO;

namespace ClipboardManager.Services;

internal static class AppPaths
{
    public const string AppName = "ClipboardManager";

    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
    public static readonly string Images = Path.Combine(Root, "images");
    public static readonly string Thumbs = Path.Combine(Root, "thumbs");
    public static readonly string Db = Path.Combine(Root, "history.db");
    public static readonly string SettingsFile = Path.Combine(Root, "settings.json");
    public static readonly string Log = Path.Combine(Root, "app.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Images);
        Directory.CreateDirectory(Thumbs);
    }

    public static void LogError(string message, Exception? ex = null)
    {
        try
        {
            File.AppendAllText(Log, $"{DateTime.Now:s} {message} {ex}\n");
        }
        catch { /* логування не повинно валити програму */ }
    }
}
