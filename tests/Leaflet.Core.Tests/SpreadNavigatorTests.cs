using Leaflet.Core.Viewing;

namespace Leaflet.Core.Tests;

public class SpreadNavigatorTests
{
    private static SpreadNavigator Nav(int count, PageMode mode = PageMode.Auto, bool cover = false, params int[] wide) =>
        new(count, i => wide.Contains(i), mode, cover);

    [Fact]
    public void SpreadsPairPagesFromTheFirst()
    {
        var nav = Nav(5);
        var spreads = Walk(nav);
        Assert.Equal([new(0, 2), new(2, 2), new(4, 1)], spreads);
    }

    [Fact]
    public void CoverSingleShowsTheFirstPageAlone()
    {
        var spreads = Walk(Nav(5, cover: true));
        Assert.Equal([new(0, 1), new(1, 2), new(3, 2)], spreads);
    }

    [Fact]
    public void SingleModeShowsOnePage()
    {
        Assert.Equal([new(0, 1), new(1, 1), new(2, 1)], Walk(Nav(3, PageMode.Single)));
    }

    [Fact]
    public void AutoShowsWidePagesAlone()
    {
        // 0 1 | 2(wide) | 3 4
        Assert.Equal([new(0, 2), new(2, 1), new(3, 2)], Walk(Nav(5, PageMode.Auto, false, 2)));
        // 0 | 1(wide) | 2 3
        Assert.Equal([new(0, 1), new(1, 1), new(2, 2)], Walk(Nav(4, PageMode.Auto, false, 1)));
    }

    [Fact]
    public void SpreadModePairsWidePagesToo()
    {
        Assert.Equal([new(0, 2), new(2, 2)], Walk(Nav(4, PageMode.Spread, false, 1)));
    }

    [Fact]
    public void NextAndPreviousStopAtTheEnds()
    {
        var nav = Nav(4);
        Assert.Null(nav.Next(new Spread(2, 2)));
        Assert.Null(nav.Previous(new Spread(0, 2)));
    }

    [Fact]
    public void PreviousPairsTheTwoPagesBefore()
    {
        var nav = Nav(6);
        Assert.Equal(new Spread(2, 2), nav.Previous(new Spread(4, 2)));
        // After stepping one page the spreads are 1-2, 3-4; going back from 1-2 shows page 0 alone.
        Assert.Equal(new Spread(0, 1), nav.Previous(new Spread(1, 2)));
    }

    [Fact]
    public void PreviousDoesNotPairTheCoverOrAWidePage()
    {
        Assert.Equal(new Spread(1, 1), Nav(4, cover: true).Previous(new Spread(2, 2)));
        Assert.Equal(new Spread(3, 1), Nav(6, PageMode.Auto, false, 2).Previous(new Spread(4, 2)));
    }

    [Fact]
    public void StepMovesOnePageAndRepairs()
    {
        var nav = Nav(5);
        Assert.Equal(new Spread(1, 2), nav.StepForward(new Spread(0, 2)));
        Assert.Equal(new Spread(0, 2), nav.StepBack(new Spread(1, 2)));
        Assert.Null(nav.StepForward(new Spread(4, 1)));
        Assert.Null(nav.StepBack(new Spread(0, 2)));
    }

    [Fact]
    public void LastSpreadEndsWithTheLastPage()
    {
        Assert.Equal(new Spread(3, 2), Nav(5).LastSpread());
        Assert.Equal(new Spread(4, 1), Nav(5, PageMode.Auto, false, 4).LastSpread());
        Assert.Equal(new Spread(0, 1), Nav(1).LastSpread());
    }

    private static List<Spread> Walk(SpreadNavigator nav)
    {
        var list = new List<Spread>();
        for (Spread? s = nav.FirstSpread(); s is { } x; s = nav.Next(x)) list.Add(x);
        return list;
    }
}
