using System.IO.Compression;
using System.Text;
using Leaflet.Core.Books;

namespace Leaflet.Core.Tests;

public sealed class BookWalkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LeafletTests", Guid.NewGuid().ToString("N"));

    public BookWalkerTests()
    {
        // root\
        //   a\  1.zip  2.zip  10.zip  readme.txt
        //   b\  (images)  sub\ (images)
        //   c\  empty\
        //   d\  3.zip
        Archive("a\\1.zip");
        Archive("a\\2.zip");
        Archive("a\\10.zip");
        File.WriteAllText(Combine("a\\readme.txt"), "x");
        Image("b\\1.jpg");
        Image("b\\sub\\1.jpg");
        Directory.CreateDirectory(Combine("c\\empty"));
        Archive("d\\3.zip");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void NextIsTheNextArchiveInNaturalOrder()
    {
        Assert.Equal(Combine("a\\2.zip"), BookWalker.Find(Combine("a\\1.zip"), forward: true));
        Assert.Equal(Combine("a\\10.zip"), BookWalker.Find(Combine("a\\2.zip"), forward: true));
    }

    [Fact]
    public void AtTheEndOfAFolderNextGoesToTheNextFolder()
    {
        Assert.Equal(Combine("b"), BookWalker.Find(Combine("a\\10.zip"), forward: true));
        Assert.Equal(Combine("b\\sub"), BookWalker.Find(Combine("b"), forward: true));
        // c has no images anywhere: skipped.
        Assert.Equal(Combine("d\\3.zip"), BookWalker.Find(Combine("b\\sub"), forward: true));
    }

    [Fact]
    public void PreviousWalksBack()
    {
        Assert.Equal(Combine("b\\sub"), BookWalker.Find(Combine("d\\3.zip"), forward: false));
        Assert.Equal(Combine("b"), BookWalker.Find(Combine("b\\sub"), forward: false));
        Assert.Equal(Combine("a\\10.zip"), BookWalker.Find(Combine("b"), forward: false));
        Assert.Equal(Combine("a\\1.zip"), BookWalker.Find(Combine("a\\2.zip"), forward: false));
    }

    [Fact]
    public void FromAFolderInsideAnArchiveMovesOnFromTheArchive()
    {
        Assert.Equal(Combine("a\\2.zip"), BookWalker.Find(Combine("a\\1.zip\\vol1"), forward: true));
    }

    [Fact]
    public void OpenFolderInsideArchive()
    {
        var path = Combine("x.zip");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create, false, Encoding.UTF8))
        {
            foreach (var name in new[] { "vol1/1.jpg", "vol1/2.jpg", "vol2/1.jpg", "vol2/ch/3.jpg" }) zip.CreateEntry(name);
        }

        Assert.Equal(["vol1", "vol2", "vol2\\ch"], ArchiveBook.ListImageFolders(path));
        using var book = BookOpener.Open(Combine("x.zip\\vol2"));
        Assert.Equal(["vol2\\1.jpg", "vol2\\ch\\3.jpg"], book.Pages.Select(p => p.Name));
        Assert.Equal(Combine("x.zip\\vol2"), book.Location);
    }

    private string Combine(string relative) => Path.Combine(_root, relative);

    private void Archive(string relative)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Combine(relative))!);
        using var zip = new ZipArchive(File.Create(Combine(relative)), ZipArchiveMode.Create);
        zip.CreateEntry("1.jpg");
    }

    private void Image(string relative)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Combine(relative))!);
        File.WriteAllBytes(Combine(relative), [1]);
    }
}
