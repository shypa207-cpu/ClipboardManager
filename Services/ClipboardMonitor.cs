using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardManager.Native;

namespace ClipboardManager.Services;

public sealed class CapturedClip
{
    public string? Text { get; init; }
    public BitmapSource? Image { get; init; }
}

public sealed class ClipboardMonitor : IDisposable
{
    private const int MaxAttempts = 10;
    private const int MaxTextChars = 2_000_000;

    private readonly HiddenWindow _window;
    private readonly DispatcherTimer _debounce;
    private bool _listening;

    public event Action<CapturedClip>? Captured;

    /// <summary>Ігнорувати оновлення буфера до цього моменту (щоб не ловити власні записи).</summary>
    public DateTime IgnoreUntilUtc { get; set; } = DateTime.MinValue;

    public ClipboardMonitor(HiddenWindow window)
    {
        _window = window;
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await ReadWithRetryAsync();
        };
        _window.AddHook(WndProc);
        _listening = NativeMethods.AddClipboardFormatListener(_window.Handle);
        if (!_listening) AppPaths.LogError("AddClipboardFormatListener failed");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            _debounce.Stop();
            _debounce.Start();
        }
        return IntPtr.Zero;
    }

    private async Task ReadWithRetryAsync()
    {
        if (DateTime.UtcNow < IgnoreUntilUtc) return;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                var clip = TryRead();
                if (clip != null) Captured?.Invoke(clip);
                return;
            }
            catch (ExternalException) // CLIPBRD_E_CANT_OPEN — буфер тимчасово зайнятий іншим процесом
            {
                await Task.Delay(40 * (attempt + 1));
            }
            catch (Exception ex)
            {
                AppPaths.LogError("Clipboard read", ex);
                return;
            }
        }
        AppPaths.LogError("Clipboard read: gave up after retries");
    }

    private static CapturedClip? TryRead()
    {
        var data = System.Windows.Clipboard.GetDataObject();
        if (data == null) return null;

        // Менеджери паролів просять не зберігати вміст
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return null;

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = data.GetData(DataFormats.UnicodeText) as string;
            if (!string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextChars)
                return new CapturedClip { Text = text };
            return null;
        }

        if (System.Windows.Clipboard.ContainsImage())
        {
            var bmp = System.Windows.Clipboard.GetImage();
            if (bmp != null)
            {
                bmp.Freeze();
                return new CapturedClip { Image = bmp };
            }
        }
        return null;
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_listening) NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        _window.RemoveHook(WndProc);
    }
}
