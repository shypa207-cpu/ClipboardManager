using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardManager.Services;

namespace ClipboardManager.Models;

public sealed class ClipVm : INotifyPropertyChanged
{
    private BitmapImage? _thumb;
    private bool _thumbLoaded;

    public ClipVm(ClipItem item) { Item = item; }

    public ClipItem Item { get; }
    public long Id => Item.Id;
    public bool IsImage => Item.Kind == ClipKind.Image;

    public string Preview => (Item.Text ?? "").Trim();
    public Visibility TextVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ImageVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;

    public BitmapImage? Thumbnail
    {
        get
        {
            if (!_thumbLoaded && IsImage && Item.ThumbPath != null)
            {
                _thumb = ImageStore.Load(Item.ThumbPath);
                _thumbLoaded = true;
            }
            return _thumb;
        }
    }

    public bool IsPinned
    {
        get => Item.IsPinned;
        set
        {
            if (Item.IsPinned == value) return;
            Item.IsPinned = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinGlyph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinTip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinnedVisibility)));
        }
    }

    // E718 = Pin, E77A = Unpin (Segoe Fluent Icons)
    public string PinGlyph => IsPinned ? "" : "";
    public string PinTip => IsPinned ? "Відкріпити" : "Закріпити";
    public Visibility PinnedVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
}
