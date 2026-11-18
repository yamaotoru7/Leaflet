namespace Leaflet.Core.Books;

/// <summary>
/// Finds the book before or after a book in the file system, as Leeyes does when reading past the
/// last page: the next archive or image folder in the same folder, going into subfolders and, at the
/// end of a folder, on to the folder after it.
/// </summary>
/// <remarks>
/// Books in a folder are its subfolders and archives in natural name order. A folder that holds
/// images itself comes before its subfolders. Whether an archive holds images is not checked here;
/// the caller skips books that turn out to be empty.
/// </remarks>
public static class BookWalker
{
    /// <summary>The location of the next (or previous) book, or null when there is none.</summary>
    /// <param name="location">The current book: a folder, an archive, or a folder inside an archive.</param>
    public static string? Find(string location, bool forward)
    {
        var current = Path.GetFullPath(location).TrimEnd('\\');
        if (!Directory.Exists(current) && !File.Exists(current) && BookOpener.SplitArchivePath(current) is var (archive, _))
        {
            current = archive;  // From a folder inside an archive, move on from the archive.
        }

        // Going forward from a folder book, its own subfolders come next.
        if (forward && Directory.Exists(current))
        {
            foreach (var child in Children(current))
            {
                if (FirstBookIn(child, forward) is { } found) return found;
            }
        }

        while (Path.GetDirectoryName(current) is { } parent)
        {
            var siblings = Children(parent);
            var index = siblings.FindIndex(s => string.Equals(s, current, StringComparison.OrdinalIgnoreCase));
            var after = forward ? siblings.Skip(index + 1) : siblings.Take(Math.Max(index, 0)).Reverse();
            foreach (var sibling in after)
            {
                if (FirstBookIn(sibling, forward) is { } found) return found;
            }
            // Backward past the first subfolder: the parent's own images come before its subfolders.
            if (!forward && HasImages(parent)) return parent;
            current = parent;
        }
        return null;
    }

    /// <summary>The first (or last) book in an archive or folder tree.</summary>
    private static string? FirstBookIn(string path, bool forward)
    {
        if (File.Exists(path)) return path;  // an archive
        if (forward && HasImages(path)) return path;
        var children = Children(path);
        if (!forward) children.Reverse();
        foreach (var child in children)
        {
            if (FirstBookIn(child, forward) is { } found) return found;
        }
        return !forward && HasImages(path) ? path : null;
    }

    /// <summary>Subfolders and archives of a folder in natural order. Unreadable folders have none.</summary>
    private static List<string> Children(string folder)
    {
        try
        {
            var dir = new DirectoryInfo(folder);
            return dir.EnumerateFileSystemInfos()
                .Where(i => i is DirectoryInfo d ? !d.Attributes.HasFlag(FileAttributes.System) : FileTypes.IsArchive(i.Name))
                .OrderBy(i => i.Name, NaturalComparer.Instance)
                .Select(i => i.FullName)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool HasImages(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder).Any(FileTypes.IsImage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
