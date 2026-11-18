namespace Leaflet.Core.Books;

/// <summary>Which file names are pages and which are archives.</summary>
public static class FileTypes
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib",
        ".tif", ".tiff", ".webp", ".avif", ".jxl", ".jxr", ".wdp", ".ico",
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".cbz", ".rar", ".cbr", ".7z", ".cb7",
    };

    public static bool IsImage(string name) => ImageExtensions.Contains(Path.GetExtension(name));

    public static bool IsArchive(string name) => ArchiveExtensions.Contains(Path.GetExtension(name));
}
