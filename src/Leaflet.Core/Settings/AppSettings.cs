using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leaflet.Core.Viewing;

namespace Leaflet.Core.Settings;

/// <summary>Where a book was left: the page by index and by name (the name wins if the book changed).</summary>
public sealed record BookPosition(string Location, int PageIndex, string PageName, DateTime Time);

/// <summary>A bookmark: a page of a book.</summary>
public sealed record Bookmark(string Location, int PageIndex, string PageName, DateTime Added);

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

/// <summary>Everything kept between runs, saved as JSON in %LOCALAPPDATA%\Leaflet\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>How many books remember their last page.</summary>
    public const int MaxPositions = 1000;

    public const int MaxRecentShown = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,  // keep Japanese paths readable
        Converters = { new JsonStringEnumConverter() },
    };

    public WindowPlacement? Window { get; set; }

    /// <summary>Width of the folder tree and file list.</summary>
    public double LeftPaneWidth { get; set; } = 460;

    /// <summary>Height of the folder tree as a share of the left pane (the file list gets the rest).</summary>
    public double TreeShare { get; set; } = 0.6;

    public PageMode Mode { get; set; } = PageMode.Auto;

    public bool CoverSingle { get; set; }

    public LayoutOptions Layout { get; set; } = new();

    /// <summary>"HighQuality", "Linear" or "NearestNeighbor" (WPF BitmapScalingMode names).</summary>
    public string Scaling { get; set; } = "HighQuality";

    /// <summary>Reading past the last page opens the next book.</summary>
    public bool MoveToNextBook { get; set; } = true;

    /// <summary>The book open when the app was closed, reopened at start.</summary>
    public string? LastLocation { get; set; }

    /// <summary>Last page of each book, most recent first.</summary>
    public List<BookPosition> Positions { get; set; } = [];

    public List<Bookmark> Bookmarks { get; set; } = [];

    /// <summary>The installed copy checks the GitHub releases at start, at most once per Updater.Interval.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>Records where a book is now. The book moves to the front of the recent list.</summary>
    public void Remember(string location, int pageIndex, string pageName, DateTime now)
    {
        Positions.RemoveAll(p => SameLocation(p.Location, location));
        Positions.Insert(0, new BookPosition(location, pageIndex, pageName, now));
        if (Positions.Count > MaxPositions) Positions.RemoveRange(MaxPositions, Positions.Count - MaxPositions);
    }

    /// <summary>
    /// The page to reopen a book at: the remembered page, found by name if the pages have changed.
    /// A book left at its last page starts again from the beginning. Null when there is nothing to resume.
    /// </summary>
    public int? ResumePage(string location, IReadOnlyList<string> pageNames, int lastSpreadFirst)
    {
        var position = Positions.FirstOrDefault(p => SameLocation(p.Location, location));
        if (position is null) return null;
        var index = position.PageIndex < pageNames.Count && pageNames[position.PageIndex] == position.PageName
            ? position.PageIndex
            : pageNames.ToList().FindIndex(n => string.Equals(n, position.PageName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        return index >= lastSpreadFirst ? null : index;
    }

    /// <summary>Recently read books, newest first.</summary>
    public IEnumerable<BookPosition> Recent => Positions.Take(MaxRecentShown);

    public void AddBookmark(string location, int pageIndex, string pageName, DateTime now)
    {
        Bookmarks.RemoveAll(b => SameLocation(b.Location, location) && b.PageName == pageName);
        Bookmarks.Add(new Bookmark(location, pageIndex, pageName, now));
    }

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings();
    }

    /// <summary>Writes to a temporary file first so that a crash while saving does not lose the old settings.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    public static bool SameLocation(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
