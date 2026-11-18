namespace Leaflet.Core.Books;

public static class BookOpener
{
    /// <summary>
    /// Opens a folder or an archive. For an image file, opens the folder that holds it.
    /// </summary>
    /// <remarks>A path that continues inside an archive ("a.zip\vol1") opens that folder of the archive.</remarks>
    public static IBook Open(string path)
    {
        path = Path.GetFullPath(path.Trim().Trim('"'));
        if (Directory.Exists(path)) return new FolderBook(path);
        if (File.Exists(path))
        {
            if (FileTypes.IsArchive(path)) return new ArchiveBook(path);
            if (FileTypes.IsImage(path)) return new FolderBook(Path.GetDirectoryName(path)!);
            throw new NotSupportedException($"開けない種類のファイルです: {Path.GetFileName(path)}");
        }
        if (SplitArchivePath(path) is var (archive, inner)) return new ArchiveBook(archive, inner);
        throw new FileNotFoundException($"ファイルが見つかりません: {path}", path);
    }

    /// <summary>
    /// Splits "C:\x\a.zip\vol1\ch2" into the archive file and the folder inside it,
    /// or returns null when no part of the path is an archive file.
    /// </summary>
    public static (string Archive, string InnerFolder)? SplitArchivePath(string path)
    {
        path = Path.GetFullPath(path).TrimEnd('\\');
        for (var i = path.LastIndexOf('\\'); i > 2; i = path.LastIndexOf('\\', i - 1))
        {
            var head = path[..i];
            if (FileTypes.IsArchive(head) && File.Exists(head)) return (head, path[(i + 1)..]);
        }
        return null;
    }

    /// <summary>
    /// The page to show first when <paramref name="path"/> names an image inside a folder; otherwise 0.
    /// </summary>
    public static int InitialPage(IBook book, string path)
    {
        if (book is not FolderBook || !File.Exists(path)) return 0;
        var name = Path.GetFileName(path);
        for (var i = 0; i < book.Pages.Count; i++)
        {
            if (string.Equals(book.Pages[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return 0;
    }
}
