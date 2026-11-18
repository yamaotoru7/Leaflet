namespace Leaflet.Core.Viewing;

/// <summary>How pages are put side by side.</summary>
public enum PageMode
{
    /// <summary>One page at a time.</summary>
    Single,

    /// <summary>Always two pages.</summary>
    Spread,

    /// <summary>Two pages, but a wide (landscape) image is shown alone.</summary>
    Auto,
}

/// <summary>The pages shown together: <see cref="Count"/> pages from <see cref="First"/> in reading order.</summary>
public readonly record struct Spread(int First, int Count)
{
    public int Last => First + Count - 1;

    public IEnumerable<int> Pages => Enumerable.Range(First, Count);
}

/// <summary>
/// Decides which pages make up a spread and where the page-turning operations go.
/// Pure logic: the caller supplies whether a page is wider than tall.
/// </summary>
/// <param name="pageCount">Number of pages in the book.</param>
/// <param name="isWide">Whether a page is landscape. Pages whose size is not known yet count as not wide.</param>
/// <param name="mode">Single, spread or automatic.</param>
/// <param name="coverSingle">Show the first page alone and start spreads from the second.</param>
public sealed class SpreadNavigator(int pageCount, Func<int, bool> isWide, PageMode mode, bool coverSingle)
{
    public int PageCount => pageCount;

    /// <summary>The spread that starts at <paramref name="first"/>.</summary>
    public Spread At(int first)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(first, pageCount);
        return first + 1 < pageCount && CanPair(first, first + 1) ? new Spread(first, 2) : new Spread(first, 1);
    }

    /// <summary>The spread after <paramref name="current"/>, or null at the end of the book.</summary>
    public Spread? Next(Spread current)
    {
        var first = current.First + current.Count;
        return first < pageCount ? At(first) : null;
    }

    /// <summary>The spread before <paramref name="current"/>, or null at the start of the book.</summary>
    public Spread? Previous(Spread current) => current.First > 0 ? EndingAt(current.First - 1) : null;

    /// <summary>One page forward: the second page of a spread becomes the first. Null on the last page.</summary>
    public Spread? StepForward(Spread current) => current.First + 1 < pageCount ? At(current.First + 1) : null;

    /// <summary>One page back. Null on the first page.</summary>
    public Spread? StepBack(Spread current) => current.First > 0 ? At(current.First - 1) : null;

    public Spread FirstSpread() => At(0);

    /// <summary>The spread that ends with the last page.</summary>
    public Spread LastSpread() => EndingAt(pageCount - 1);

    private Spread EndingAt(int last) =>
        last > 0 && CanPair(last - 1, last) ? new Spread(last - 1, 2) : new Spread(last, 1);

    private bool CanPair(int a, int b) => mode switch
    {
        PageMode.Single => false,
        _ when coverSingle && a == 0 => false,
        PageMode.Spread => true,
        _ => !isWide(a) && !isWide(b),
    };
}
