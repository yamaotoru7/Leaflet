using System.ComponentModel;
using System.IO;
using Leaflet.Core.Books;

namespace Leaflet.App;

/// <summary>A row of the file list.</summary>
public sealed class PageItem(int index, PageEntry entry) : INotifyPropertyChanged
{
    private bool _isShown;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; } = index;

    public string Name { get; } = entry.FileName;

    public PageEntry Entry { get; } = entry;

    public string SizeText { get; } = $"{(entry.Size + 1023) / 1024:N0} KB";

    public long Size { get; } = entry.Size;

    public string TypeText { get; } = Path.GetExtension(entry.Name).TrimStart('.').ToUpperInvariant() + " ファイル";

    public string DateText { get; } = entry.LastModified?.ToString("yyyy/MM/dd HH:mm") ?? "";

    /// <summary>The page is on screen now (shown in red, as in Leeyes).</summary>
    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (_isShown == value) return;
            _isShown = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
        }
    }
}
