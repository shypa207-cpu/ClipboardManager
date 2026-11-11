using System;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClipboardManager.Models;
using ClipboardManager.Native;
using ClipboardManager.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace ClipboardManager;

public partial class App : Application
{
    private Mutex? _mutex;
    private HiddenWindow? _hidden;
    private Storage _storage = null!;
    private Settings _settings = null!;
    private ClipboardMonitor? _monitor;
    private HotkeyService? _hotkey;
    private MainWindow _main = null!;
    private WinForms.NotifyIcon? _tray;

    public static bool IsDark { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            AppPaths.EnsureCreated();
            StartCore();
        }
        catch (Exception ex)
        {
            AppPaths.LogError("Startup", ex);
            System.Windows.MessageBox.Show(ex.ToString(), "Clipboard Manager — помилка запуску",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void StartCore()
    {
        _mutex = new Mutex(true, @"Local\ClipboardManager.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }

        DispatcherUnhandledException += (_, a) =>
        {
            AppPaths.LogError("UI exception", a.Exception);
            a.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            AppPaths.LogError("Fatal", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            AppPaths.LogError("Task exception", a.Exception);
            a.SetObserved();
        };

        AppPaths.EnsureCreated();
        _settings = Settings.Load();
        _settings.AutoStart = Autostart.IsEnabled();

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += (_, a) =>
        {
            if (a.Category == UserPreferenceCategory.General)
                Dispatcher.BeginInvoke(new Action(ApplyTheme));
        };

        _storage = new Storage();
        _storage.Trim(_settings.HistoryLimit);

        _hidden = new HiddenWindow();
        _monitor = new ClipboardMonitor(_hidden);
        _monitor.Captured += OnCaptured;

        _main = new MainWindow(_storage, _monitor);
        _main.ApplyBackdrop(IsDark);

        _hotkey = new HotkeyService(_hidden);
        _hotkey.Pressed += OnHotkey;
        bool hotkeyOk = _hotkey.Register(_settings.HotkeyModifiers, _settings.HotkeyKey);

        CreateTray();
        if (!hotkeyOk)
            _tray!.ShowBalloonTip(5000, "Clipboard Manager",
                "Не вдалося зареєструвати гарячу клавішу — вона зайнята іншою програмою. " +
                "Змініть її у файлі settings.json.", WinForms.ToolTipIcon.Warning);

        _main.WarmUp();

        if (hotkeyOk)
            _tray!.ShowBalloonTip(3000, "Clipboard Manager",
                "Працює у фоні. Натисніть Ctrl+Shift+V, щоб відкрити історію.", WinForms.ToolTipIcon.Info);
    }

    private void OnHotkey()
    {
        if (_main.IsVisible) { _main.HideWindow(); return; }

        IntPtr prev = NativeMethods.GetForegroundWindow();
        var (x, y, _) = CaretLocator.GetAnchor();
        _main.ShowAt(x, y, prev);
    }

    private async void OnCaptured(CapturedClip clip)
    {
        try
        {
            ClipItem item;
            if (clip.Text != null)
            {
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clip.Text)));
                item = new ClipItem { Kind = ClipKind.Text, Text = clip.Text, Hash = "t:" + hash };
            }
            else if (clip.Image != null)
            {
                var img = clip.Image;
                var r = await Task.Run(() => ImageStore.Save(img));
                item = new ClipItem
                {
                    Kind = ClipKind.Image,
                    ImagePath = r.ImagePath,
                    ThumbPath = r.ThumbPath,
                    Hash = "i:" + r.Hash
                };
            }
            else return;

            _storage.Add(item, _settings.HistoryLimit);
            if (_main.IsVisible) _main.Reload();
        }
        catch (Exception ex) { AppPaths.LogError("Capture", ex); }
    }

    private void ApplyTheme()
    {
        bool light = true;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (k?.GetValue("AppsUseLightTheme") is int v) light = v != 0;
        }
        catch { }

        IsDark = !light;
        var dict = new ResourceDictionary
        {
            Source = new Uri(light ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative)
        };
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(dict);
        _main?.ApplyBackdrop(IsDark);
    }

    // ---------- Системний трей ----------
    private void CreateTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Відкрити", null, (_, _) => ShowFromTray());

        var limit = new WinForms.ToolStripMenuItem("Розмір історії");
        foreach (int n in new[] { 25, 50, 100, 200 })
        {
            var mi = new WinForms.ToolStripMenuItem(n.ToString()) { Checked = _settings.HistoryLimit == n };
            mi.Click += (_, _) =>
            {
                _settings.HistoryLimit = n;
                _settings.Save();
                _storage.Trim(n);
                foreach (WinForms.ToolStripMenuItem other in limit.DropDownItems)
                    other.Checked = other.Text == n.ToString();
            };
            limit.DropDownItems.Add(mi);
        }
        menu.Items.Add(limit);

        var auto = new WinForms.ToolStripMenuItem("Автозапуск з Windows") { Checked = _settings.AutoStart };
        auto.Click += (_, _) =>
        {
            auto.Checked = !auto.Checked;
            _settings.AutoStart = auto.Checked;
            Autostart.Set(auto.Checked);
            _settings.Save();
        };
        menu.Items.Add(auto);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Вийти", null, (_, _) => Quit());

        _tray = new WinForms.NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "Clipboard Manager (Ctrl+Shift+V)",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.MouseClick += (_, a) => { if (a.Button == WinForms.MouseButtons.Left) ShowFromTray(); };
    }

    private void ShowFromTray()
    {
        NativeMethods.GetCursorPos(out var p);
        _main.ShowAt(p.X, p.Y, IntPtr.Zero);
    }

    private static Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var board = new SolidBrush(Color.FromArgb(0, 103, 192));
            using var paper = new SolidBrush(Color.White);
            using var clip = new SolidBrush(Color.FromArgb(40, 40, 40));
            g.FillRectangle(board, 5, 5, 22, 25);
            g.FillRectangle(paper, 9, 10, 14, 16);
            g.FillRectangle(clip, 11, 2, 10, 7);
            using var pen = new Pen(Color.FromArgb(0, 103, 192), 2);
            g.DrawLine(pen, 11, 15, 21, 15);
            g.DrawLine(pen, 11, 19, 21, 19);
            g.DrawLine(pen, 11, 23, 18, 23);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void Quit()
    {
        _tray!.Visible = false;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _hotkey?.Dispose();
            _monitor?.Dispose();
            _hidden?.Dispose();
            _tray?.Dispose();
            _mutex?.ReleaseMutex();
        }
        catch { }
        base.OnExit(e);
    }
}
