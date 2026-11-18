using System.IO;
using System.Windows.Media.Imaging;
using Leaflet.Core.Books;

namespace Leaflet.App.Imaging;

/// <summary>
/// Reads and decodes the pages of one book in the background and keeps the ones near the current page.
/// A page that fails to read or decode becomes a <see cref="PageImage.Failed"/> page; nothing is thrown.
/// </summary>
public sealed class PageCache : IDisposable
{
    private readonly IBook _book;
    private readonly Dictionary<int, Task<PageImage>> _pages = [];
    private readonly SemaphoreSlim _decoders = new(2);
    private bool _disposed;

    public PageCache(IBook book) => _book = book;

    public IBook Book => _book;

    /// <summary>Starts loading the page if needed and returns it when ready.</summary>
    public Task<PageImage> Get(int index)
    {
        lock (_pages)
        {
            if (!_pages.TryGetValue(index, out var task))
            {
                task = Task.Run(() => Load(index));
                _pages[index] = task;
            }
            return task;
        }
    }

    /// <summary>The page if it has finished loading.</summary>
    public PageImage? Peek(int index)
    {
        lock (_pages)
        {
            return _pages.TryGetValue(index, out var task) && task.IsCompletedSuccessfully ? task.Result : null;
        }
    }

    /// <summary>Starts loading the pages in the range and forgets loaded pages outside it.</summary>
    public void Prefetch(int first, int last)
    {
        first = Math.Max(first, 0);
        last = Math.Min(last, _book.Pages.Count - 1);
        lock (_pages)
        {
            foreach (var index in _pages.Keys.Where(i => i < first || i > last).ToList())
            {
                _pages.Remove(index);
            }
        }
        for (var i = first; i <= last; i++) Get(i);
    }

    /// <summary>Closes the book once the loads in progress have finished.</summary>
    public void Dispose()
    {
        Task[] pending;
        lock (_pages)
        {
            if (_disposed) return;
            _disposed = true;
            pending = [.. _pages.Values];
            _pages.Clear();
        }
        Task.WhenAll(pending).ContinueWith(_ => _book.Dispose(), TaskScheduler.Default);
    }

    private async Task<PageImage> Load(int index)
    {
        var name = _book.Pages[index].Name;
        await _decoders.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_pages)
            {
                if (_disposed) return PageImage.Failed(index, name, "閉じられました。");
            }
            var bytes = _book.ReadPage(index);
            return PageImage.Loaded(index, name, Decode(bytes));
        }
        catch (Exception ex)
        {
            return PageImage.Failed(index, name, ex.Message);
        }
        finally
        {
            _decoders.Release();
        }
    }

    /// <summary>Decodes with WIC, the codecs installed in Windows.</summary>
    private static BitmapSource Decode(byte[] bytes)
    {
        if (bytes.Length == 0) throw new InvalidDataException("空のファイルです。");
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
