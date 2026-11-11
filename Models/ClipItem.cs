using System;

namespace ClipboardManager.Models;

public enum ClipKind { Text = 0, Image = 1 }

public sealed class ClipItem
{
    public long Id { get; set; }
    public ClipKind Kind { get; set; }
    public string? Text { get; set; }
    public string? ImagePath { get; set; }
    public string? ThumbPath { get; set; }
    public string Hash { get; set; } = "";
    public bool IsPinned { get; set; }
    public DateTime CreatedUtc { get; set; }
}
