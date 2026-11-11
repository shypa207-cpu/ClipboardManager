using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClipboardManager.Models;
using ClipboardManager.Native;
using ClipboardManager.Services;

namespace ClipboardManager;

public partial class MainWindow : Window
{
    private readonly Storage _storage;
    private readonly ClipboardMonitor _monitor;
    private readonly ObservableCollection<ClipVm> _items = new();
    private IntPtr _targetWindow;
    private IntPtr _hwnd;

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    public MainWindow(Storage storage, ClipboardMonitor monitor)
    {
        _storage = storage;
        _monitor = monitor;
        InitializeComponent();
        List.ItemsSource = _items;

        SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            var src = HwndSource.FromHwnd(h);
            if (src?.CompositionTarget != null)
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
            int ex = NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(h, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW);
        };
        _hwnd = new WindowInteropHelper(this).EnsureHandle();

        // Автоматичне приховування при втраті фокусу (клік поза вікном)
        Deactivated += (_, _) => HideWindow();
    }

    // ---------- Mica / Acrylic, заокруглення, тема ----------
    public void ApplyBackdrop(bool dark)
    {
        if (_hwnd == IntPtr.Zero) return;
        int d = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int));

        if (Environment.OSVersion.Version.Build >= 22621) // Windows 11 22H2+
        {
            int round = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int acrylic = NativeMethods.DWMSBT_TRANSIENTWINDOW;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, sizeof(int));
            Background = Brushes.Transparent;
        }
        else
        {
            // Windows 10 / ранні збірки Win11: суцільний фон
            Background = (Brush)FindResource("FallbackBg");
        }
    }

    /// <summary>Прогрів: один раз показуємо вікно за межами екрана, щоб перший виклик був швидким.</summary>
    public void WarmUp()
    {
        ShowActivated = false;
        Show();
        UpdateLayout();
        Hide();
        ShowActivated = true;
    }

    // ---------- Показ / приховування ----------
    public void ShowAt(int x, int y, IntPtr previousWindow)
    {
        _targetWindow = previousWindow;
        Reload();

        // Переносимо вікно на потрібний монітор, щоб WPF підхопив його DPI
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(Width * dpi.DpiScaleX);
        int h = (int)Math.Round(Height * dpi.DpiScaleY);

        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea;
        int nx = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
        int ny = y;
        if (ny + h > wa.Bottom)
        {
            int above = y - h - (int)(28 * dpi.DpiScaleY); // над кареткою
            ny = above >= wa.Top ? above : Math.Max(wa.Top, wa.Bottom - h);
        }

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, nx, ny, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        Show();
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FocusFirst));
    }

    public void HideWindow()
    {
        if (!IsVisible) return;
        Hide();
        _items.Clear(); // звільняємо візуали та мініатюри, поки вікно сховане
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }));
    }

    private void FocusFirst()
    {
        if (_items.Count == 0) { Focus(); return; }
        List.SelectedIndex = 0;
        List.ScrollIntoView(_items[0]);
        List.UpdateLayout();
        (List.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
    }

    // ---------- Дані ----------
    public void Reload()
    {
        long? selected = (List.SelectedItem as ClipVm)?.Id;
        _items.Clear();
        foreach (var it in _storage.GetAll()) _items.Add(new ClipVm(it));
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearBtn.IsEnabled = _items.Count > 0;

        if (selected != null)
            foreach (var vm in _items)
                if (vm.Id == selected) { List.SelectedItem = vm; break; }
    }

    // ---------- Взаємодія ----------
    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ClipVm vm })
        {
            e.Handled = true;
            _ = PasteAsync(vm);
        }
    }

    private void OnItemButton(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: ClipVm vm } btn) return;
        e.Handled = true;

        switch (btn.Tag as string)
        {
            case "pin":
                _storage.SetPinned(vm.Id, !vm.IsPinned);
                Reload();
                break;
            case "delete":
                _storage.Delete(vm.Id);
                Reload();
                break;
        }
    }

    private void OnClearAll(object sender, RoutedEventArgs e)
    {
        _storage.ClearUnpinned();
        Reload();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                HideWindow();
                break;
            case Key.Enter when List.SelectedItem is ClipVm vm:
                e.Handled = true;
                _ = PasteAsync(vm);
                break;
            case Key.Delete when List.SelectedItem is ClipVm vm:
                e.Handled = true;
                int idx = List.SelectedIndex;
                _storage.Delete(vm.Id);
                Reload();
                if (_items.Count > 0) List.SelectedIndex = Math.Min(idx, _items.Count - 1);
                break;
            case Key.Down or Key.Up when !List.IsKeyboardFocusWithin && _items.Count > 0:
                e.Handled = true;
                FocusFirst();
                break;
        }
    }

    private async Task PasteAsync(ClipVm vm)
    {
        try
        {
            // Власний запис у буфер не повинен потрапити в історію як новий елемент
            _monitor.IgnoreUntilUtc = DateTime.UtcNow.AddMilliseconds(800);

            bool ok;
            if (vm.IsImage)
            {
                var img = vm.Item.ImagePath != null ? ImageStore.Load(vm.Item.ImagePath) : null;
                ok = img != null && PasteService.SetImage(img);
            }
            else
            {
                string text = _storage.GetFullText(vm.Id) ?? vm.Preview;
                ok = PasteService.SetText(text);
            }

            var target = _targetWindow;
            HideWindow();
            if (!ok) return;

            if (target != IntPtr.Zero) NativeMethods.SetForegroundWindow(target);
            await Task.Delay(90); // даємо цільовому вікну отримати фокус
            PasteService.SendCtrlV();
        }
        catch (Exception ex) { AppPaths.LogError("Paste", ex); }
    }
}
