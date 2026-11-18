using Leaflet.Core.Settings;
using Leaflet.Core.Viewing;

namespace Leaflet.Core.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LeafletTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ResumesAtTheRememberedPage()
    {
        var settings = new AppSettings();
        settings.Remember(@"R:\よつばと！ 第01巻.zip", 2, "0003.jpg", Now);

        Assert.Equal(2, settings.ResumePage(@"r:\よつばと！ 第01巻.zip", ["0001.jpg", "0002.jpg", "0003.jpg", "0004.jpg"], 3));
    }

    [Fact]
    public void FindsThePageByNameWhenTheBookChanged()
    {
        var settings = new AppSettings();
        settings.Remember("a.zip", 2, "c.jpg", Now);

        Assert.Equal(1, settings.ResumePage("a.zip", ["a.jpg", "c.jpg", "d.jpg", "e.jpg"], 3));
        Assert.Null(settings.ResumePage("a.zip", ["x.jpg", "y.jpg"], 1));
    }

    [Fact]
    public void ABookFinishedStartsFromTheBeginning()
    {
        var settings = new AppSettings();
        settings.Remember("a.zip", 2, "c.jpg", Now);

        Assert.Null(settings.ResumePage("a.zip", ["a.jpg", "b.jpg", "c.jpg"], lastSpreadFirst: 1));
    }

    [Fact]
    public void RecentListKeepsEachBookOnceNewestFirst()
    {
        var settings = new AppSettings();
        settings.Remember("a.zip", 0, "1.jpg", Now);
        settings.Remember("b.zip", 0, "1.jpg", Now);
        settings.Remember("A.ZIP", 5, "6.jpg", Now);

        Assert.Equal(["A.ZIP", "b.zip"], settings.Recent.Select(p => p.Location));
    }

    [Fact]
    public void SavesAndLoadsEverything()
    {
        var path = Path.Combine(_dir, "settings.json");
        var settings = new AppSettings
        {
            Mode = PageMode.Single,
            CoverSingle = true,
            Layout = new LayoutOptions(FitMode.Width, NoEnlarge: false, EqualHeight: false, Binding.Left),
            Window = new WindowPlacement(10, 20, 800, 600, true),
            LastLocation = @"R:\本\🌻.zip",
        };
        settings.Remember(@"R:\本\🌻.zip", 3, "4.jpg", Now);
        settings.AddBookmark(@"R:\本\🌻.zip", 3, "4.jpg", Now);
        settings.Save(path);

        var loaded = AppSettings.Load(path);

        Assert.Equal(PageMode.Single, loaded.Mode);
        Assert.True(loaded.CoverSingle);
        Assert.Equal(settings.Layout, loaded.Layout);
        Assert.Equal(settings.Window, loaded.Window);
        Assert.Equal(@"R:\本\🌻.zip", loaded.LastLocation);
        Assert.Equal(settings.Positions, loaded.Positions);
        Assert.Equal(settings.Bookmarks, loaded.Bookmarks);
        Assert.Contains("本", File.ReadAllText(path));
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var loaded = AppSettings.Load(Path.Combine(_dir, "none.json"));
        Assert.Equal(PageMode.Auto, loaded.Mode);
        Assert.Equal(Binding.Right, loaded.Layout.Binding);
    }
}
