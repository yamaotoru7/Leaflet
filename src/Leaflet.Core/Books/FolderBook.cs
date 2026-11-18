namespace Leaflet.Core.Books;

/// <summary>The images directly inside a folder.</summary>
public sealed class FolderBook : IBook
{
    public FolderBook(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        var dir = new DirectoryInfo(Path);
        Pages = dir.EnumerateFiles()
            .Where(f => FileTypes.IsImage(f.Name))
            .OrderBy(f => f.Name, NaturalComparer.Instance)
            .Select(f => new PageEntry(f.Name, f.Length, f.LastWriteTime))
            .ToList();
    }

    public string Path { get; }

    public IReadOnlyList<PageEntry> Pages { get; }

    public byte[] ReadPage(int index) => File.ReadAllBytes(System.IO.Path.Combine(Path, Pages[index].Name));

    public void Dispose()
    {
    }
}
