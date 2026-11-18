namespace Leaflet.Core.Viewing;

/// <summary>How a spread is scaled to the view.</summary>
public enum FitMode
{
    /// <summary>The whole spread fits in the view.</summary>
    Window,

    /// <summary>The spread fills the view width.</summary>
    Width,

    /// <summary>The spread fills the view height.</summary>
    Height,

    /// <summary>One image pixel per screen pixel.</summary>
    Original,
}

/// <summary>Reading direction of a book.</summary>
public enum Binding
{
    /// <summary>Right-to-left: the earlier page is on the right (Japanese manga).</summary>
    Right,

    /// <summary>Left-to-right: the earlier page is on the left.</summary>
    Left,
}

/// <summary>Where one page is drawn, in screen pixels.</summary>
/// <param name="Slot">Position of the page in the spread in reading order (0 or 1).</param>
public readonly record struct PagePlacement(int Slot, double X, double Y, double Width, double Height, double Scale);

public sealed record LayoutOptions(FitMode Fit = FitMode.Window, bool NoEnlarge = true, bool EqualHeight = true, Binding Binding = Binding.Right);

public static class SpreadLayout
{
    /// <summary>
    /// Places the pages of a spread side by side with no gap and centres them in the view.
    /// </summary>
    /// <param name="pages">Pixel sizes of the pages in reading order (1 or 2).</param>
    /// <param name="viewWidth">View width in screen pixels.</param>
    /// <param name="viewHeight">View height in screen pixels.</param>
    /// <returns>Placements from left to right on screen.</returns>
    public static IReadOnlyList<PagePlacement> Compute(IReadOnlyList<(double Width, double Height)> pages, double viewWidth, double viewHeight, LayoutOptions options)
    {
        if (pages.Count == 0 || viewWidth <= 0 || viewHeight <= 0) return [];

        // Per-page factor that makes the heights equal (relative to the tallest page).
        var tallest = pages.Max(p => p.Height);
        var unit = pages.Select(p => options.EqualHeight && options.Fit != FitMode.Original ? tallest / p.Height : 1.0).ToArray();
        var groupWidth = pages.Select((p, i) => p.Width * unit[i]).Sum();
        var groupHeight = pages.Select((p, i) => p.Height * unit[i]).Max();

        var scale = options.Fit switch
        {
            FitMode.Window => Math.Min(viewWidth / groupWidth, viewHeight / groupHeight),
            FitMode.Width => viewWidth / groupWidth,
            FitMode.Height => viewHeight / groupHeight,
            _ => 1.0,
        };
        if (options.NoEnlarge) scale = Math.Min(scale, 1.0 / unit.Max());

        var totalWidth = groupWidth * scale;
        var totalHeight = groupHeight * scale;
        var x = totalWidth < viewWidth ? (viewWidth - totalWidth) / 2 : 0;
        var top = totalHeight < viewHeight ? (viewHeight - totalHeight) / 2 : 0;

        var order = Enumerable.Range(0, pages.Count);
        if (options.Binding == Binding.Right) order = order.Reverse();

        var result = new List<PagePlacement>(pages.Count);
        foreach (var slot in order)
        {
            var s = scale * unit[slot];
            var w = pages[slot].Width * s;
            var h = pages[slot].Height * s;
            result.Add(new PagePlacement(slot, x, top + (totalHeight - h) / 2, w, h, s));
            x += w;
        }
        return result;
    }
}
