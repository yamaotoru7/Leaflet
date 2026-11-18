using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Leaflet.App.Imaging;
using Leaflet.Core.Viewing;

namespace Leaflet.App.Controls;

/// <summary>Draws one spread (one or two pages) on a black background.</summary>
public sealed class SpreadView : FrameworkElement
{
    /// <summary>Size used to lay out a page that failed to load: a portrait page.</summary>
    private static readonly (double Width, double Height) FailedPageSize = (1000, 1414);

    private IReadOnlyList<PageImage> _pages = [];
    private LayoutOptions _options = new();

    public SpreadView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    /// <summary>The pages of the spread in reading order.</summary>
    public IReadOnlyList<PageImage> Pages
    {
        get => _pages;
        set
        {
            _pages = value;
            InvalidateVisual();
        }
    }

    public LayoutOptions Options
    {
        get => _options;
        set
        {
            _options = value;
            InvalidateVisual();
        }
    }

    public BitmapScalingMode ScalingMode
    {
        get => RenderOptions.GetBitmapScalingMode(this);
        set
        {
            RenderOptions.SetBitmapScalingMode(this, value);
            InvalidateVisual();
        }
    }

    /// <summary>Display scale of each page in reading order, from the last render.</summary>
    public IReadOnlyList<double> Scales { get; private set; } = [];

    public event EventHandler? Rendered;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
        if (_pages.Count == 0)
        {
            Scales = [];
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var sizes = _pages.Select(p => p.Bitmap is null ? FailedPageSize : (p.PixelWidth, p.PixelHeight)).ToList();
        var placements = SpreadLayout.Compute(sizes, ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY, _options);

        var scales = new double[_pages.Count];
        foreach (var p in placements)
        {
            var rect = new Rect(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY, p.Width / dpi.DpiScaleX, p.Height / dpi.DpiScaleY);
            var page = _pages[p.Slot];
            scales[p.Slot] = p.Scale;
            if (page.Bitmap is { } bitmap)
            {
                dc.DrawImage(bitmap, rect);
            }
            else
            {
                DrawError(dc, rect, page, dpi.PixelsPerDip);
            }
        }
        Scales = scales;
        Rendered?.Invoke(this, EventArgs.Empty);
    }

    private static void DrawError(DrawingContext dc, Rect rect, PageImage page, double pixelsPerDip)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), null, rect);
        var text = new FormattedText(
            $"読み込めません\n{page.Name}\n{page.Error}",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"),
            14,
            Brushes.Gainsboro,
            pixelsPerDip)
        {
            MaxTextWidth = Math.Max(rect.Width - 32, 1),
            TextAlignment = TextAlignment.Center,
        };
        dc.DrawText(text, new Point(rect.X + 16, rect.Y + (rect.Height - text.Height) / 2));
    }
}
