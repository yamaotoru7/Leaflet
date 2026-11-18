using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Leaflet.Core.Books;

/// <summary>
/// The images in a ZIP / RAR / 7z archive, read without extracting it to disk.
/// </summary>
/// <remarks>
/// ZIP names with the UTF-8 flag are read as UTF-8 and names without it as Shift_JIS (CP932),
/// which is what Japanese Windows tools wrote. RAR and 7z store names in Unicode.
/// <para>
/// In a solid archive an entry can only be decompressed by decompressing everything before it,
/// so reading pages one by one costs time in proportion to the page number. A solid archive is
/// therefore decompressed once from start to end in the background and its pages are kept in memory.
/// </para>
/// </remarks>
public sealed class ArchiveBook : IBook
{
    private readonly IArchive _archive;
    private readonly List<IArchiveEntry> _entries;
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource<byte[]>[]? _solidPages;
    private readonly Task? _solidReader;
    private readonly CancellationTokenSource _closing = new();

    static ArchiveBook()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <param name="path">The archive file.</param>
    /// <param name="innerFolder">A folder inside the archive ("a\b"); only the images under it are pages. "" for all.</param>
    public ArchiveBook(string path, string innerFolder = "")
    {
        Path = System.IO.Path.GetFullPath(path);
        InnerFolder = innerFolder.Replace('/', '\\').Trim('\\');
        var prefix = InnerFolder.Length == 0 ? "" : InnerFolder + "\\";
        _archive = OpenArchive(Path);
        try
        {
            _entries = _archive.Entries
                .Where(e => !e.IsDirectory && e.Key is not null && FileTypes.IsImage(e.Key))
                .Where(e => NormalizeName(e.Key!).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(e => (Entry: e, Name: NormalizeName(e.Key!)))
                .OrderBy(x => x.Name, PathComparer.Instance)
                .Select(x => x.Entry)
                .ToList();
            Pages = _entries
                .Select(e => new PageEntry(NormalizeName(e.Key!), e.Size, e.LastModifiedTime))
                .ToList();
            if (_archive.IsSolid)
            {
                _solidPages = _entries.Select(_ => new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
                _solidReader = Task.Run(ReadSolidArchive);
            }
        }
        catch
        {
            _archive.Dispose();
            throw;
        }
    }

    public string Path { get; }

    /// <summary>The folder inside the archive this book shows, or "".</summary>
    public string InnerFolder { get; }

    public string Location => InnerFolder.Length == 0 ? Path : Path + "\\" + InnerFolder;

    public IReadOnlyList<PageEntry> Pages { get; }

    /// <summary>
    /// The folders inside an archive that hold images, directly or below them, e.g. "a" and "a\b".
    /// </summary>
    public static IReadOnlyList<string> ListImageFolders(string path)
    {
        using var archive = OpenArchive(path);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || entry.Key is null || !FileTypes.IsImage(entry.Key)) continue;
            var name = NormalizeName(entry.Key);
            for (var i = name.LastIndexOf('\\'); i > 0; i = name.LastIndexOf('\\', i - 1))
            {
                if (!folders.Add(name[..i])) break;
            }
        }
        return folders.Order(NaturalComparer.Instance).ToList();
    }

    private static IArchive OpenArchive(string path) =>
        ArchiveFactory.OpenArchive(path, new ReaderOptions
        {
            ArchiveEncoding = new ArchiveEncoding { Default = Encoding.GetEncoding(932) },
        });

    /// <summary>Whether pages come from the background decompression of a solid archive.</summary>
    public bool IsSolid => _solidPages is not null;

    public byte[] ReadPage(int index)
    {
        if (_solidPages is not null) return _solidPages[index].Task.GetAwaiter().GetResult();

        var entry = _entries[index];
        // Archive readers share one file stream, so entries are read one at a time.
        lock (_lock)
        {
            using var stream = entry.OpenEntryStream();
            using var buffer = new MemoryStream(entry.Size is > 0 and < int.MaxValue ? (int)entry.Size : 0);
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    public void Dispose()
    {
        _closing.Cancel();
        try
        {
            _solidReader?.Wait();
        }
        catch (AggregateException)
        {
            // The failure was already handed to the pages.
        }
        _archive.Dispose();
    }

    /// <summary>Decompresses the whole solid archive in order and hands each page to its waiters.</summary>
    private void ReadSolidArchive()
    {
        var pages = _solidPages!;
        var byKey = _entries.Select((e, i) => (e.Key!, i)).GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => new Queue<int>(g.Select(x => x.i)));
        try
        {
            using var reader = _archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                _closing.Token.ThrowIfCancellationRequested();
                var entry = reader.Entry;
                if (entry.IsDirectory || entry.Key is null || !byKey.TryGetValue(entry.Key, out var queue) || queue.Count == 0) continue;
                var index = queue.Dequeue();
                try
                {
                    using var stream = reader.OpenEntryStream();
                    using var buffer = new MemoryStream(entry.Size is > 0 and < int.MaxValue ? (int)entry.Size : 0);
                    stream.CopyTo(buffer);
                    pages[index].TrySetResult(buffer.ToArray());
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A broken entry in a solid stream usually breaks the rest too; report it and try to go on.
                    pages[index].TrySetException(ex);
                }
            }
            foreach (var page in pages) page.TrySetException(new InvalidDataException("書庫の中にこのファイルが見つかりません。"));
        }
        catch (Exception ex)
        {
            foreach (var page in pages) page.TrySetException(ex is OperationCanceledException ? new ObjectDisposedException(Path) : ex);
        }
    }

    private static string NormalizeName(string key) => key.Replace('/', '\\').TrimStart('\\');

    /// <summary>Sorts by folder first, then by name, each in natural order.</summary>
    private sealed class PathComparer : IComparer<string>
    {
        public static readonly PathComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var xs = x!.Split('\\');
            var ys = y!.Split('\\');
            for (var i = 0; i < Math.Min(xs.Length, ys.Length); i++)
            {
                var xIsFile = i == xs.Length - 1;
                var yIsFile = i == ys.Length - 1;
                if (xIsFile != yIsFile) return xIsFile ? -1 : 1;  // files before subfolders
                var c = NaturalComparer.Instance.Compare(xs[i], ys[i]);
                if (c != 0) return c;
            }
            return xs.Length.CompareTo(ys.Length);
        }
    }
}
