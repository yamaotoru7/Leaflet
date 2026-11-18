using Leaflet.Core.Viewing;

namespace Leaflet.Core.Tests;

public class SpreadLayoutTests
{
    [Fact]
    public void RightBindingPutsTheFirstPageOnTheRightAndCentresTheSpread()
    {
        // Same as the Leeyes screenshot: 878x1232 and 962x1232 at 75%.
        var placements = SpreadLayout.Compute([(878, 1232), (962, 1232)], 2000, 924, new LayoutOptions());

        Assert.Equal([1, 0], placements.Select(p => p.Slot));
        Assert.All(placements, p => Assert.Equal(0.75, p.Scale, 3));
        var left = placements[0];
        var right = placements[1];
        Assert.Equal(left.X + left.Width, right.X, 6);  // no gap
        Assert.Equal(2000 - (right.X + right.Width), left.X, 6);  // centred
        Assert.Equal(0, left.Y, 6);
    }

    [Fact]
    public void LeftBindingPutsTheFirstPageOnTheLeft()
    {
        var placements = SpreadLayout.Compute([(100, 200), (100, 200)], 1000, 1000, new LayoutOptions(Binding: Binding.Left));
        Assert.Equal([0, 1], placements.Select(p => p.Slot));
    }

    [Fact]
    public void NoEnlargeKeepsSmallImagesAtOriginalSize()
    {
        var p = Assert.Single(SpreadLayout.Compute([(100, 200)], 1000, 1000, new LayoutOptions()));
        Assert.Equal(1.0, p.Scale);
        Assert.Equal((450, 400), (p.X, p.Y));
    }

    [Fact]
    public void EnlargeFillsTheWindow()
    {
        var p = Assert.Single(SpreadLayout.Compute([(100, 200)], 1000, 1000, new LayoutOptions(NoEnlarge: false)));
        Assert.Equal(5.0, p.Scale);
    }

    [Fact]
    public void EqualHeightMatchesTheHeights()
    {
        // With "no enlarge" the shorter page is not scaled up; the taller one is scaled down instead.
        var placements = SpreadLayout.Compute([(500, 1000), (400, 800)], 10000, 1000, new LayoutOptions());
        Assert.All(placements, p => Assert.Equal(800, p.Height, 6));

        placements = SpreadLayout.Compute([(500, 1000), (400, 800)], 10000, 1000, new LayoutOptions(NoEnlarge: false));
        Assert.All(placements, p => Assert.Equal(1000, p.Height, 6));
    }

    [Fact]
    public void FitWidthFillsTheWidthEvenIfTallerThanTheView()
    {
        var p = Assert.Single(SpreadLayout.Compute([(1000, 3000)], 500, 800, new LayoutOptions(Fit: FitMode.Width)));
        Assert.Equal(0.5, p.Scale);
        Assert.Equal(0, p.Y);
    }
}
