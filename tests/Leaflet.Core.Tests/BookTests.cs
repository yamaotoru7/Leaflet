using System.IO.Compression;
using System.Text;
using Leaflet.Core.Books;

namespace Leaflet.Core.Tests;

public sealed class BookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LeafletTests", Guid.NewGuid().ToString("N"));

    public BookTests()
    {
        Directory.CreateDirectory(_dir);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void NaturalOrderPutsTwoBeforeTen()
    {
        string[] names = ["10.jpg", "2.jpg", "1.jpg"];
        Assert.Equal(["1.jpg", "2.jpg", "10.jpg"], names.Order(NaturalComparer.Instance));
    }

    [Fact]
    public void ShiftJisNamesWithoutUtf8FlagAreDecoded()
    {
        var path = Path.Combine(_dir, "sjis.zip");
        // A non-UTF-8 entry encoding leaves the UTF-8 flag off, as old Japanese zip tools did.
        WriteZip(path, Encoding.GetEncoding(932), "よつばと\\第01話.jpg", "よつばと\\第02話.jpg");

        using var book = new ArchiveBook(path);

        Assert.Equal(["よつばと\\第01話.jpg", "よつばと\\第02話.jpg"], book.Pages.Select(p => p.Name));
    }

    [Fact]
    public void Utf8NamesAreDecoded()
    {
        var path = Path.Combine(_dir, "utf8.zip");
        WriteZip(path, Encoding.UTF8, "🌻ひまわり.png", "髙橋①.png");

        using var book = new ArchiveBook(path);

        Assert.Equal(["🌻ひまわり.png", "髙橋①.png"], book.Pages.Select(p => p.Name).Order());
    }

    [Fact]
    public void ArchivePagesAreImagesInNaturalOrderWithFilesBeforeSubfolders()
    {
        var path = Path.Combine(_dir, "order.zip");
        WriteZip(path, Encoding.UTF8, "b\\1.jpg", "10.jpg", "2.jpg", "readme.txt", "a\\1.jpg");

        using var book = new ArchiveBook(path);

        Assert.Equal(["2.jpg", "10.jpg", "a\\1.jpg", "b\\1.jpg"], book.Pages.Select(p => p.Name));
    }

    [Fact]
    public void ReadPageReturnsEntryBytes()
    {
        var path = Path.Combine(_dir, "bytes.zip");
        WriteZip(path, Encoding.UTF8, "1.jpg");

        using var book = new ArchiveBook(path);

        Assert.Equal(Encoding.UTF8.GetBytes("1.jpg"), book.ReadPage(0));
    }

    [Fact]
    public void FolderWithUnicodeNameIsRead()
    {
        var folder = Path.Combine(_dir, "📚 漫画 ﾃｽﾄ");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "10.png"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "9.png"), [2]);
        File.WriteAllBytes(Path.Combine(folder, "note.txt"), [3]);

        using var book = BookOpener.Open(Path.Combine(folder, "10.png"));

        Assert.Equal(["9.png", "10.png"], book.Pages.Select(p => p.Name));
        Assert.Equal(1, BookOpener.InitialPage(book, Path.Combine(folder, "10.png")));
        Assert.Equal([1], book.ReadPage(1));
    }

    [Fact]
    public void LongPathFolderIsRead()
    {
        var folder = _dir;
        while (folder.Length < 300) folder = Path.Combine(folder, new string('長', 40));
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "1.jpg"), [7]);

        using var book = BookOpener.Open(folder);

        Assert.Equal([7], book.ReadPage(0));
    }

    [Fact]
    public void BrokenEntryThrowsButOtherPagesStayReadable()
    {
        var path = Path.Combine(_dir, "broken.zip");
        WriteZip(path, Encoding.UTF8, "1.jpg", "2.jpg");
        CorruptFirstEntryData(path);

        using var book = new ArchiveBook(path);

        Assert.ThrowsAny<Exception>(() => book.ReadPage(0));
        Assert.Equal(Encoding.UTF8.GetBytes("2.jpg"), book.ReadPage(1));
    }

    [Fact]
    public void SolidSevenZipPagesAreReadFromTheBackgroundDecompression()
    {
        // Needs 7-Zip to make the archive; skipped where it is not installed.
        const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
        if (!File.Exists(sevenZip)) return;
        var src = Path.Combine(_dir, "src");
        Directory.CreateDirectory(src);
        for (var i = 1; i <= 30; i++) File.WriteAllText(Path.Combine(src, $"{i}.jpg"), $"page {i}");
        var path = Path.Combine(_dir, "solid.7z");
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sevenZip, ["a", "-ms=on", path, Path.Combine(src, "*")]) { RedirectStandardOutput = true }))
        {
            p!.StandardOutput.ReadToEnd();
            p.WaitForExit();
        }

        using var book = new ArchiveBook(path);

        Assert.True(book.IsSolid);
        Assert.Equal("page 30", Encoding.UTF8.GetString(book.ReadPage(29)));
        Assert.Equal("page 2", Encoding.UTF8.GetString(book.ReadPage(1)));
    }

    [Fact]
    public void NotAnArchiveThrowsOnOpen()
    {
        var path = Path.Combine(_dir, "fake.zip");
        File.WriteAllBytes(path, [0, 1, 2, 3]);

        Assert.ThrowsAny<Exception>(() => new ArchiveBook(path));
    }

    /// <summary>Writes a zip whose entries contain their own names as data.</summary>
    private static void WriteZip(string path, Encoding nameEncoding, params string[] names)
    {
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create, leaveOpen: false, nameEncoding);
        foreach (var name in names)
        {
            var entry = zip.CreateEntry(name.Replace('\\', '/'), CompressionLevel.Optimal);
            using var s = entry.Open();
            var data = Encoding.UTF8.GetBytes(name);
            s.Write(data);
        }
    }

    /// <summary>Overwrites the compressed data of the first entry so that it fails to inflate or its CRC check.</summary>
    private static void CorruptFirstEntryData(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var nameLength = BitConverter.ToUInt16(bytes, 26);
        var extraLength = BitConverter.ToUInt16(bytes, 28);
        var dataStart = 30 + nameLength + extraLength;
        var compressedSize = BitConverter.ToInt32(bytes, 18);
        for (var i = dataStart; i < dataStart + compressedSize; i++) bytes[i] = 0xFF;
        File.WriteAllBytes(path, bytes);
    }
}
