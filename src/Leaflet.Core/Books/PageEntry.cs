namespace Leaflet.Core.Books;

/// <summary>One image in a book.</summary>
/// <param name="Name">Path inside the book, with '\' separators (e.g. "vol1\0001.jpg").</param>
/// <param name="Size">Uncompressed size in bytes.</param>
/// <param name="LastModified">Last write time, if known.</param>
public sealed record PageEntry(string Name, long Size, DateTime? LastModified)
{
    /// <summary>File name without the folder part.</summary>
    public string FileName => Name[(Name.LastIndexOf('\\') + 1)..];

    /// <summary>Folder part inside the book, or "" for a page at the top.</summary>
    public string Folder => Name.LastIndexOf('\\') is var i and >= 0 ? Name[..i] : "";
}
