namespace Leaflet.Core.Books;

/// <summary>A folder or an archive whose images are read as pages.</summary>
public interface IBook : IDisposable
{
    /// <summary>Full path of the folder or archive file.</summary>
    string Path { get; }

    /// <summary>
    /// Where the book is, as shown in the address bar: the folder, the archive, or a folder inside an
    /// archive ("C:\books\a.zip\vol1"). Opening this path opens the same book.
    /// </summary>
    string Location => Path;

    /// <summary>Images in reading order.</summary>
    IReadOnlyList<PageEntry> Pages { get; }

    /// <summary>
    /// Reads the raw bytes of a page. Safe to call from several threads.
    /// Throws if the page cannot be read (a broken entry); the book stays usable.
    /// </summary>
    byte[] ReadPage(int index);
}
