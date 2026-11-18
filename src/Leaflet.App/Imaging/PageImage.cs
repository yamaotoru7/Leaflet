using System.Windows.Media.Imaging;

namespace Leaflet.App.Imaging;

/// <summary>A decoded page, or the reason it could not be decoded.</summary>
public sealed class PageImage
{
    private PageImage(int index, string name, BitmapSource? bitmap, string? error)
    {
        Index = index;
        Name = name;
        Bitmap = bitmap;
        Error = error;
    }

    public int Index { get; }

    public string Name { get; }

    /// <summary>Frozen bitmap, or null when the page failed.</summary>
    public BitmapSource? Bitmap { get; }

    /// <summary>Why the page could not be shown, or null.</summary>
    public string? Error { get; }

    public int PixelWidth => Bitmap?.PixelWidth ?? 0;

    public int PixelHeight => Bitmap?.PixelHeight ?? 0;

    public int BitsPerPixel => Bitmap?.Format.BitsPerPixel ?? 0;

    /// <summary>Landscape pages are shown alone in automatic spread mode. Failed pages count as portrait.</summary>
    public bool IsWide => Bitmap is not null && PixelWidth > PixelHeight;

    public static PageImage Loaded(int index, string name, BitmapSource bitmap) => new(index, name, bitmap, null);

    public static PageImage Failed(int index, string name, string error) => new(index, name, null, error);
}
