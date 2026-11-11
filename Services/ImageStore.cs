using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipboardManager.Services;

public static class ImageStore
{
    private const double MaxThumbW = 480, MaxThumbH = 160;

    /// <summary>Зберігає повне зображення та мініатюру як PNG на диск. bmp має бути Frozen.</summary>
    public static (string Hash, string ImagePath, string ThumbPath) Save(BitmapSource bmp)
    {
        bmp = EnsureVisible(bmp);
        byte[] bytes = Encode(bmp);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string img = Path.Combine(AppPaths.Images, hash + ".png");
        string thumb = Path.Combine(AppPaths.Thumbs, hash + ".png");

        if (!File.Exists(img)) File.WriteAllBytes(img, bytes);
        if (!File.Exists(thumb))
        {
            double scale = Math.Min(1.0, Math.Min(MaxThumbW / bmp.PixelWidth, MaxThumbH / bmp.PixelHeight));
            BitmapSource small = bmp;
            if (scale < 1.0)
            {
                var tb = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
                tb.Freeze();
                small = tb;
            }
            File.WriteAllBytes(thumb, Encode(small));
        }
        return (hash, img, thumb);
    }

    /// <summary>
    /// Скріншоти часто приходять як 32-бітний DIB з нульовим альфа-каналом — такі зображення
    /// стають повністю прозорими. Якщо всі альфа-байти нульові, робимо їх непрозорими.
    /// </summary>
    private static BitmapSource EnsureVisible(BitmapSource src)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);

        bool anyAlpha = false;
        for (int i = 3; i < px.Length; i += 4)
            if (px[i] != 0) { anyAlpha = true; break; }
        if (anyAlpha) return src;

        for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        var fixedBmp = BitmapSource.Create(w, h, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, px, stride);
        fixedBmp.Freeze();
        return fixedBmp;
    }

    private static byte[] Encode(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Завантажує зображення з диска без утримання файлу відкритим.</summary>
    public static BitmapImage? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception ex) { AppPaths.LogError("Image load", ex); return null; }
    }
}
