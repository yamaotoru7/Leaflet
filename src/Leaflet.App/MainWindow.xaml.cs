using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Leaflet.App.Imaging;
using Leaflet.App.Tree;
using Leaflet.App.Updates;
using Leaflet.Core.Updates;
using System.Net.Http;
using Leaflet.Core.Books;
using Leaflet.Core.Settings;
using Leaflet.Core.Viewing;
using Microsoft.Win32;

namespace Leaflet.App;

public partial class MainWindow : Window
{
    /// <summary>Pages decoded ahead of and behind the current spread (Leeyes: 4 and 4).</summary>
    private const int PrefetchAhead = 4;
    private const int PrefetchBehind = 4;

    private readonly SemaphoreSlim _navigation = new(1);
    private readonly FolderTreeModel _tree = new();
    private int _queuedMoves;
    private bool _switchingBook;

    /// <summary>The image was closed with Esc while the book stays open in the list.</summary>
    private bool _imageClosed;

    /// <summary>Reading past the last page opens the next book (Leeyes: [MOVE] LastFileMove=1).</summary>
    private bool _moveToNextBook = true;
    private PageCache? _cache;
    private List<PageItem> _items = [];
    private Spread _spread;
    private int _wheelDelta;

    private PageMode _mode = PageMode.Auto;
    private bool _coverSingle;
    private LayoutOptions _layout = new();

    private bool _isFullScreen;
    private GridLength _leftWidth;
    private WindowState _restoreState;

    /// <summary>
    /// %LOCALAPPDATA%\Leaflet\settings.json. The LEAFLET_SETTINGS environment variable points elsewhere,
    /// so that a development build can be tried without touching the settings of the installed app.
    /// </summary>
    private static readonly string SettingsPath =
        Environment.GetEnvironmentVariable("LEAFLET_SETTINGS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Leaflet", "settings.json");

    private readonly AppSettings _settings;
    private readonly string? _settingsError;

    private ReleaseInfo? _availableUpdate;
    private bool _checkingUpdate;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            _settings = AppSettings.Load(SettingsPath);
        }
        catch (Exception ex)
        {
            // Broken settings: start with the defaults and say so, rather than refusing to start.
            _settings = new AppSettings();
            _settingsError = "設定を読めなかったため、既定の設定で起動しました: " + ex.Message;
        }
        ApplySettings();
        Closing += (_, _) => SaveSettings();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseDown += (_, _) => ShowMenuForAWhile();
        Drop += OnDrop;
        Viewer.Rendered += (_, _) => UpdateTitle();
        Closed += (_, _) => _cache?.Dispose();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        // A drive can come or go while the window is not looking (e.g. a network drive mapped elsewhere).
        Activated += (_, _) =>
        {
            if (_tree.Roots.Count > 0 && _tree.DrivesChanged()) _ = _tree.RefreshDrivesAsync();
        };
    }

    private const int WM_DEVICECHANGE = 0x0219;

    private DispatcherTimer? _driveTimer;

    /// <summary>A drive or a disc was added or removed: list the drives once the burst of messages settles.</summary>
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE && _tree.Roots.Count > 0)
        {
            if (_driveTimer is null)
            {
                _driveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _driveTimer.Tick += (_, _) =>
                {
                    _driveTimer.Stop();
                    _ = _tree.RefreshDrivesAsync();
                };
            }
            _driveTimer.Stop();
            _driveTimer.Start();
        }
        return 0;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Viewer.Focus();
        FolderTree.ItemsSource = _tree.Roots;
        await _tree.LoadDrivesAsync();
        if (_settingsError is not null) StatusText.Text = _settingsError;
        var args = Environment.GetCommandLineArgs();
        CheckForUpdatesOnStartup();
        if (args.Length > 1) await OpenAsync(args[1]);
        else if (_settings.LastLocation is { } last) await OpenAsync(last);
    }

    // ---- Updates ----------------------------------------------------------------------------

    /// <summary>The installed copy checks the GitHub releases at most once per Updater.Interval, without dialogs.</summary>
    private async void CheckForUpdatesOnStartup()
    {
        if (!_settings.CheckForUpdates || !InstalledCopy.IsThisOne || !Updater.IsDue(_settings.LastUpdateCheck, DateTimeOffset.Now)) return;
        await Task.Delay(TimeSpan.FromSeconds(5));
        await CheckForUpdatesAsync(interactive: false);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is { } known) OfferUpdate(known);
        else await CheckForUpdatesAsync(interactive: true);
    }

    private void UpdateAvailable_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is { } known) OfferUpdate(known);
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        CheckUpdateMenu.IsEnabled = false;
        if (interactive) StatusText.Text = "更新を確認しています…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var latest = await Updater.GetLatestAsync(timeout.Token);
            _settings.LastUpdateCheck = DateTimeOffset.Now;
            if (latest is not null && Updater.IsNewer(latest, Updater.CurrentVersion))
            {
                latest = await Updater.WithNotesSinceAsync(latest, Updater.CurrentVersion, timeout.Token);
                _availableUpdate = latest;
                UpdateAvailableMenu.Header = $"{latest.Version} に更新(_N)";
                UpdateAvailableMenu.ToolTip = $"Leaflet {latest.Version} があります（現在 {Updater.CurrentVersion}）。";
                UpdateAvailableMenu.Visibility = Visibility.Visible;
                if (interactive) OfferUpdate(latest);
            }
            else if (interactive)
            {
                MessageBox.Show(this, $"Leaflet {Updater.CurrentVersion} は最新です。", "更新の確認", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        // A connection lost while reading the response surfaces as HttpIOException (an IOException).
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
        {
            System.Diagnostics.Trace.TraceWarning($"更新を確認できませんでした。 {error}");
            if (interactive)
            {
                MessageBox.Show(this, $"更新を確認できませんでした。\n{error.Message}", "更新の確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            _checkingUpdate = false;
            CheckUpdateMenu.IsEnabled = true;
            if (interactive && StatusText.Text == "更新を確認しています…") StatusText.Text = "";
        }
    }

    private void OfferUpdate(ReleaseInfo release)
    {
        var dialog = new UpdateWindow(release) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.InstallerPath is not { } installer) return;
        try
        {
            InstalledCopy.StartInstaller(installer, Path.GetDirectoryName(Path.GetFullPath(SettingsPath))!);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"インストーラーを起動できませんでした。\n{error.Message}", "更新", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        // Settings (and where the book was left) are saved on closing; the installer relaunches the app.
        Close();
    }

    private void ReleasesPage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            StatusText.Text = $"ブラウザーを開けませんでした: {error.Message}";
        }
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, $"Leaflet {Updater.CurrentVersion}\n\nマンガ・画像ビューアー\n{Updater.ReleasesPage}",
            "Leaflet について", MessageBoxButton.OK, MessageBoxImage.Information);

    // ---- Settings ---------------------------------------------------------------------------

    private void ApplySettings()
    {
        _mode = _settings.Mode;
        _coverSingle = _settings.CoverSingle;
        _layout = _settings.Layout;
        _moveToNextBook = _settings.MoveToNextBook;
        Viewer.Options = _layout;
        if (Enum.TryParse<BitmapScalingMode>(_settings.Scaling, out var scaling)) Viewer.ScalingMode = scaling;
        LeftColumn.Width = new GridLength(Math.Max(120, _settings.LeftPaneWidth));
        var share = Math.Clamp(_settings.TreeShare, 0.1, 0.9);
        TreeRow.Height = new GridLength(share, GridUnitType.Star);
        ListRow.Height = new GridLength(1 - share, GridUnitType.Star);

        if (_settings.Window is { } w && IsOnScreen(w))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = w.Left;
            Top = w.Top;
            Width = w.Width;
            Height = w.Height;
            if (w.Maximized) WindowState = WindowState.Maximized;
        }
    }

    /// <summary>A saved window position on a monitor that is gone would put the window out of reach.</summary>
    private static bool IsOnScreen(WindowPlacement w) =>
        w.Width >= 200 && w.Height >= 150
        && w.Left + 100 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
        && w.Top + 50 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
        && w.Left + w.Width - 100 > SystemParameters.VirtualScreenLeft
        && w.Top >= SystemParameters.VirtualScreenTop - 20;

    private void SaveSettings()
    {
        _settings.Mode = _mode;
        _settings.CoverSingle = _coverSingle;
        _settings.Layout = _layout;
        _settings.MoveToNextBook = _moveToNextBook;
        _settings.Scaling = Viewer.ScalingMode.ToString();
        if (!_isFullScreen)
        {
            _settings.LeftPaneWidth = LeftColumn.ActualWidth;
            var total = TreeRow.ActualHeight + ListRow.ActualHeight;
            if (total > 0) _settings.TreeShare = TreeRow.ActualHeight / total;
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _settings.Window = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
        }
        _settings.LastLocation = _cache?.Book.Location;
        try
        {
            _settings.Save(SettingsPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "設定を保存できませんでした: " + ex.Message, "Leaflet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- Folder tree ------------------------------------------------------------------------

    private async void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeNode node) return;
        // Selecting the node of the open book (when the tree follows the book) must not reopen it.
        if (_cache is not null && string.Equals(node.Path, _cache.Book.Location, StringComparison.OrdinalIgnoreCase)) return;
        switch (node.Kind)
        {
            case NodeKind.Archive or NodeKind.ArchiveFolder:
                await OpenAsync(node.Path, revealInTree: false);
                break;
            case NodeKind.Folder or NodeKind.Drive when node.Path.Length > 0:
                // A folder is opened only when it holds images itself, as a book of those images.
                // Any other folder closes the book that is open, so its image does not stay on screen.
                var hasImages = await Task.Run(() => HasImages(node.Path));
                if (!ReferenceEquals(FolderTree.SelectedItem, node)) return;  // another node was picked meanwhile
                if (hasImages) await OpenAsync(node.Path, revealInTree: false);
                else CloseBook(node.Path);
                break;
        }
    }

    private void FolderTreeItem_Selected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item) item.BringIntoView();
    }

    /// <summary>
    /// F5: lists the selected folder again (with the expanded folders under it) and the drives.
    /// For a node inside an archive, or a folder that is gone, the nearest folder above it is listed.
    /// With nothing selected, every expanded root is listed.
    /// </summary>
    private async Task RefreshTreeAsync()
    {
        var refreshing = _tree.RefreshDrivesAsync();
        var node = FolderTree.SelectedItem as TreeNode;
        // Directory.Exists can be slow on a share that is gone: ask in the background.
        while (node is not null && (node.Kind is NodeKind.Archive or NodeKind.ArchiveFolder || !await Task.Run(() => Directory.Exists(node.Path))))
        {
            node = node.Parent;
        }
        if (node is not null)
        {
            await node.RefreshExpandedAsync();
        }
        else
        {
            foreach (var root in _tree.Roots.ToList())
            {
                if (root.IsExpanded) await root.RefreshExpandedAsync();
            }
        }
        await refreshing;
    }

    private static bool HasImages(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder).Any(FileTypes.IsImage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Expands the tree to the open book and selects it.</summary>
    private async Task RevealInTree(string location)
    {
        var node = await _tree.RevealAsync(location);
        if (node is not null) node.IsSelected = true;
    }

    // ---- Opening ----------------------------------------------------------------------------

    /// <summary>Opens a folder, an archive, a folder inside an archive, or the folder of an image file.</summary>
    /// <param name="pageName">Open at this page (a bookmark). Otherwise an image path opens at that image,
    /// and a book opens where it was left.</param>
    private async Task OpenAsync(string path, bool revealInTree = true, string? pageName = null)
    {
        StatusText.Text = "開いています: " + path;
        IBook book;
        try
        {
            book = await Task.Run(() => BookOpener.Open(path));
        }
        catch (Exception ex)
        {
            StatusText.Text = "開けません: " + ex.Message;
            return;
        }
        if (book.Pages.Count == 0)
        {
            book.Dispose();
            StatusText.Text = "画像がありません: " + path;
            return;
        }
        var names = book.Pages.Select(p => p.Name).ToList();
        int start;
        if (pageName is not null)
        {
            start = Math.Max(0, names.FindIndex(n => string.Equals(n, pageName, StringComparison.OrdinalIgnoreCase)));
        }
        else if (FileTypes.IsImage(path) && File.Exists(path))
        {
            start = BookOpener.InitialPage(book, path);
        }
        else
        {
            start = _settings.ResumePage(book.Location, names, lastSpreadFirst: Math.Max(1, names.Count - 2)) ?? 0;
        }
        await ShowBookAsync(book, nav => nav.At(start), revealInTree);
    }

    private async Task ShowBookAsync(IBook book, Func<SpreadNavigator, Spread?> start, bool revealInTree)
    {
        _cache?.Dispose();
        _cache = new PageCache(book);
        _items = book.Pages.Select((p, i) => new PageItem(i, p)).ToList();
        FileList.ItemsSource = _items;
        AddressBox.Text = book.Location;
        _spread = default;
        StatusText.Text = "";

        var moving = MoveAsync(start);
        if (revealInTree) await RevealInTree(book.Location);
        await moving;
    }

    /// <summary>
    /// Opens the next (or previous) book next to the current one (Leeyes: move on from the last file).
    /// Going back opens the previous book at its last spread. Books without images are skipped.
    /// </summary>
    private async Task GoToBookAsync(bool forward, bool openAtEnd)
    {
        if (_cache is null || _switchingBook) return;
        _switchingBook = true;
        try
        {
            var location = _cache.Book.Location;
            for (var tries = 0; tries < 100; tries++)
            {
                StatusText.Text = forward ? "次の書庫を探しています…" : "前の書庫を探しています…";
                var from = location;
                var found = await Task.Run(() => BookWalker.Find(from, forward));
                if (found is null)
                {
                    StatusText.Text = forward ? "次の書庫はありません" : "前の書庫はありません";
                    return;
                }
                location = found;
                IBook book;
                try
                {
                    book = await Task.Run(() => BookOpener.Open(found));
                }
                catch (Exception)
                {
                    continue;  // a broken archive: try the one after it
                }
                if (book.Pages.Count == 0)
                {
                    book.Dispose();
                    continue;
                }
                // Page moves are accepted again from here: the new book's first move must go through.
                _switchingBook = false;
                await ShowBookAsync(book, nav => openAtEnd ? nav.LastSpread() : nav.FirstSpread(), revealInTree: true);
                return;
            }
        }
        finally
        {
            _switchingBook = false;
        }
    }

    // ---- Page turning -----------------------------------------------------------------------

    private SpreadNavigator CreateNavigator(PageCache cache) =>
        new(cache.Book.Pages.Count, i => cache.Peek(i)?.IsWide ?? false, _mode, _coverSingle);

    /// <summary>
    /// Moves to the spread chosen by <paramref name="move"/>. Moves run one at a time in the order requested.
    /// </summary>
    /// <param name="bookStep">At the end (or start) of the book, +1 moves on to the next book, -1 back to the previous one.</param>
    private async Task MoveAsync(Func<SpreadNavigator, Spread?> move, int bookStep = 0)
    {
        if (_cache is null || _switchingBook) return;
        var changeBook = false;
        if (Interlocked.Increment(ref _queuedMoves) > 3)
        {
            // Too many key repeats waiting: drop this one rather than lag behind.
            Interlocked.Decrement(ref _queuedMoves);
            return;
        }
        await _navigation.WaitAsync();
        try
        {
            var cache = _cache;
            if (cache is null) return;
            var count = cache.Book.Pages.Count;

            // Whether a page is landscape decides the pairing, so the sizes near the spread must be known.
            await LoadRange(cache, _spread.First - 2, _spread.First + _spread.Count + 2);
            var target = move(CreateNavigator(cache));
            if (target is null)
            {
                if (bookStep != 0 && _moveToNextBook) changeBook = true;
                else StatusText.Text = _spread.First == 0 ? "最初のページです" : "最後のページです";
                return;
            }
            // A far jump (Home, End, a click in the list) lands where sizes may be unknown: load and decide again.
            await LoadRange(cache, target.Value.First - 2, target.Value.Last + 2);
            if (cache != _cache) return;
            target = move(CreateNavigator(cache)) ?? target;

            var spread = target.Value;
            var pages = await Task.WhenAll(spread.Pages.Select(cache.Get));
            if (cache != _cache) return;
            Show(spread, pages);
            cache.Prefetch(spread.First - PrefetchBehind, spread.Last + PrefetchAhead);
        }
        finally
        {
            _navigation.Release();
            Interlocked.Decrement(ref _queuedMoves);
            if (changeBook) await GoToBookAsync(bookStep > 0, openAtEnd: bookStep < 0);
        }
    }

    private static Task LoadRange(PageCache cache, int first, int last)
    {
        first = Math.Max(first, 0);
        last = Math.Min(last, cache.Book.Pages.Count - 1);
        return Task.WhenAll(Enumerable.Range(first, Math.Max(0, last - first + 1)).Select(cache.Get));
    }

    private void Show(Spread spread, PageImage[] pages)
    {
        _imageClosed = false;
        _settings.Remember(_cache!.Book.Location, spread.First, _items[spread.First].Entry.Name, DateTime.Now);
        foreach (var item in _items.Skip(_spread.First).Take(_spread.Count)) item.IsShown = false;
        _spread = spread;
        foreach (var item in _items.Skip(spread.First).Take(spread.Count)) item.IsShown = true;

        Viewer.Pages = pages;
        // Like Leeyes, the address goes down to the folder inside the archive.
        var folder = _items[spread.First].Entry.Folder;
        AddressBox.Text = folder.Length == 0 ? _cache!.Book.Path : Path.Combine(_cache!.Book.Path, folder);
        FileList.ScrollIntoView(_items[spread.Last]);
        FileList.ScrollIntoView(_items[spread.First]);
        // While the list has the focus, its selection follows the spread on screen.
        if (FileList.IsKeyboardFocusWithin)
        {
            SelectShownFiles();
            FocusSelectedFile();
        }

        var size = _items.Skip(spread.First).Take(spread.Count).Sum(i => i.Size);
        SizeStatus.Text = $"{(size + 1023) / 1024:N0} KB";
        PageStatus.Text = $"{spread.Last + 1} / {_items.Count} ファイル";
        StatusText.Text = string.Join(" / ", pages.Where(p => p.Error is not null).Select(p => $"{p.Name}: {p.Error}"));
    }

    /// <summary>Title like Leeyes: "Leaflet - 0002.jpg (962 x 1232 x 24bit) [75%] &lt;&lt; 0001.jpg (...)".</summary>
    private void UpdateTitle()
    {
        var pages = Viewer.Pages;
        var scales = Viewer.Scales;
        if (pages.Count == 0 || scales.Count != pages.Count)
        {
            Title = "Leaflet";
            return;
        }
        var parts = pages.Select((p, i) => p.Bitmap is null
            ? $"{Path.GetFileName(p.Name)} (読み込めません)"
            : $"{Path.GetFileName(p.Name)} ({p.PixelWidth} x {p.PixelHeight} x {p.BitsPerPixel}bit) [{scales[i] * 100:0}%]");
        // Screen order, left to right.
        var ordered = _layout.Binding == Binding.Right ? parts.Reverse() : parts;
        Title = "Leaflet - " + string.Join(_layout.Binding == Binding.Right ? "  <<  " : "  >>  ", ordered);
    }

    private Task NextSpread() => MoveAsync(nav => nav.Next(_spread), bookStep: 1);

    private Task PreviousSpread() => MoveAsync(nav => nav.Previous(_spread), bookStep: -1);

    private Task StepForward() => MoveAsync(nav => nav.StepForward(_spread), bookStep: 1);

    private Task StepBack() => MoveAsync(nav => nav.StepBack(_spread), bookStep: -1);

    private Task NextBook() => GoToBookAsync(forward: true, openAtEnd: false);

    private Task PreviousBook() => GoToBookAsync(forward: false, openAtEnd: false);

    private Task FirstPage() => MoveAsync(nav => nav.FirstSpread());

    private Task LastPage() => MoveAsync(nav => nav.LastSpread());

    /// <summary>Lays out again from the current first page after the spread settings change.</summary>
    private Task Relayout() => MoveAsync(nav => nav.At(_spread.First));

    // ---- Input ------------------------------------------------------------------------------

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (_isFullScreen && !IsPageKey(key)) ShowMenuForAWhile();

        // An open menu uses the arrow keys and Enter itself.
        if (Keyboard.FocusedElement is MenuItem || MainMenu.IsKeyboardFocusWithin) return;

        // Tab and Shift+Tab move only between the address bar, the tree, the list and the image.
        if (key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            e.Handled = true;
            MoveFocus(forward: Keyboard.Modifiers == ModifierKeys.None);
            return;
        }

        if (ctrl && key == Key.O)
        {
            e.Handled = true;
            await ShowOpenDialog();
            return;
        }
        if (ctrl && key == Key.D)
        {
            e.Handled = true;
            AddBookmark();
            return;
        }
        if (key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await RefreshTreeAsync();
            return;
        }
        if (key == Key.F11)
        {
            e.Handled = true;
            ToggleFullScreen();
            return;
        }
        // Esc in full screen goes back to the window with the image closed (the list has the focus).
        if (key == Key.Escape && _isFullScreen)
        {
            e.Handled = true;
            ToggleFullScreen();
            CloseImage();
            return;
        }
        // Leeyes: Esc (and Ctrl+F4) closes the image (the book stays open in the list).
        // Esc with no image on screen quits the app.
        if ((key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) || (ctrl && key == Key.F4))
        {
            e.Handled = true;
            if (key == Key.Escape && (_cache is null || _imageClosed)) Close();
            else CloseImage();
            return;
        }

        if (ctrl && !AddressBox.IsKeyboardFocusWithin && key is Key.Down or Key.Up)
        {
            e.Handled = true;
            await (key == Key.Down ? NextBook() : PreviousBook());
            return;
        }

        // Enter toggles full screen from anywhere (also right after picking a book in the tree),
        // except in the address bar (opens the path) and in the list while the image is closed (shows it).
        if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && !AddressBox.IsKeyboardFocusWithin
            && !(_imageClosed && FileList.IsKeyboardFocusWithin))
        {
            e.Handled = true;
            ToggleFullScreen();
            return;
        }

        // In the list with the image shown, ↑ and ↓ move by spread (the selection follows the spread).
        if (FileList.IsKeyboardFocusWithin && !_imageClosed && _cache is not null
            && Keyboard.Modifiers == ModifierKeys.None && key is Key.Down or Key.Up)
        {
            e.Handled = true;
            await (key == Key.Down ? NextSpread() : PreviousSpread());
            return;
        }

        // The address bar, the tree and the list use the arrow keys themselves.
        if (AddressBox.IsKeyboardFocusWithin || FolderTree.IsKeyboardFocusWithin || FileList.IsKeyboardFocusWithin) return;

        Func<Task>? action = key switch
        {
            Key.Down or Key.PageDown => NextSpread,
            Key.Up or Key.PageUp => PreviousSpread,
            Key.Left => _layout.Binding == Binding.Right ? StepForward : StepBack,
            Key.Right => _layout.Binding == Binding.Right ? StepBack : StepForward,
            Key.Home => FirstPage,
            Key.End => LastPage,
            _ => null,
        };
        if (action is null || ctrl) return;
        e.Handled = true;
        await action();
    }

    /// <summary>The places Tab goes round, in order.</summary>
    private FrameworkElement[] FocusStops => [AddressBox, FolderTree, FileList, Viewer];

    private void MoveFocus(bool forward)
    {
        var stops = FocusStops;
        var current = Array.FindIndex(stops, s => s.IsKeyboardFocusWithin);
        for (var step = 1; step <= stops.Length; step++)
        {
            // From outside the stops (nothing focused), Tab starts at the first and Shift+Tab at the last.
            var index = current < 0
                ? (forward ? step - 1 : stops.Length - step)
                : ((current + (forward ? step : -step)) % stops.Length + stops.Length) % stops.Length;
            var stop = stops[index];
            if (stop.IsVisible && stop.IsEnabled && FocusInside(stop)) return;
        }
    }

    /// <summary>Focuses the selected item of the tree or the list (so the arrow keys go on from it), or the element itself.</summary>
    private bool FocusInside(FrameworkElement stop)
    {
        if (stop == FileList) SelectShownFiles();
        Control? container = stop switch
        {
            TreeView tree when tree.SelectedItem is TreeNode node => FindTreeItem(tree, node),
            ListView list when list.SelectedItem is { } item => list.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem,
            _ => null,
        };
        if (container is not null && container.Focus()) return true;
        if (stop is TextBox box)
        {
            box.Focus();
            box.SelectAll();
            return box.IsKeyboardFocusWithin;
        }
        return stop.Focus() || stop.IsKeyboardFocusWithin;
    }

    /// <summary>
    /// Selects the files of the spread on screen, so that the list picks up from the current page.
    /// With the image closed, the selection made by <see cref="CloseImage"/> (or moved since) is kept.
    /// </summary>
    private void SelectShownFiles()
    {
        if (_cache is null || _imageClosed || _items.Count == 0) return;
        var shown = _items.Skip(_spread.First).Take(_spread.Count).ToList();
        if (shown.Count == 0) return;
        FileList.SelectedItems.Clear();
        foreach (var item in shown) FileList.SelectedItems.Add(item);
        FileList.ScrollIntoView(shown[0]);
        FileList.UpdateLayout();  // so that the container of the first file exists to take the focus
    }

    /// <summary>Puts the focus on the (first) selected file, where the arrow keys go on from.</summary>
    private void FocusSelectedFile()
    {
        if (FileList.SelectedItem is { } item && FileList.ItemContainerGenerator.ContainerFromItem(item) is ListViewItem container)
        {
            container.Focus();
        }
    }

    private static TreeViewItem? FindTreeItem(ItemsControl parent, TreeNode node)
    {
        // The path from the root to the node, then down the generated containers.
        var chain = new Stack<TreeNode>();
        for (var n = node; n is not null; n = n.Parent) chain.Push(n);
        ItemsControl current = parent;
        while (chain.Count > 0)
        {
            if (current.ItemContainerGenerator.ContainerFromItem(chain.Pop()) is not TreeViewItem item) return null;
            current = item;
        }
        return current as TreeViewItem;
    }

    private async void Viewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _wheelDelta += e.Delta;
        while (Math.Abs(_wheelDelta) >= Mouse.MouseWheelDeltaForOneLine)
        {
            var forward = _wheelDelta < 0;
            _wheelDelta -= Math.Sign(_wheelDelta) * Mouse.MouseWheelDeltaForOneLine;
            await (forward ? NextSpread() : PreviousSpread());
        }
    }

    private void Viewer_MouseDown(object sender, MouseButtonEventArgs e) => Viewer.Focus();

    private async void FileList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is PageItem item) await GoToPage(item.Index);
    }

    private async void FileList_KeyUp(object sender, KeyEventArgs e)
    {
        if (FileList.SelectedItem is not PageItem item) return;
        // With the image closed the arrow keys only move the selection; Enter shows the image again.
        // (With the image shown, ↑ and ↓ move by spread in OnPreviewKeyDown.)
        // (With the image shown, Enter toggles full screen in OnPreviewKeyDown instead.)
        var opens = (e.Key == Key.Enter && _imageClosed)
            || (!_imageClosed && e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End);
        if (opens) await GoToPage(item.Index);
    }

    // ---- File list columns -------------------------------------------------------------------
    //
    // The name column takes the width the other columns leave, so that the list does not scroll
    // sideways while there is room. Only when the pane is narrower than the other columns plus a
    // minimum name width does the horizontal scroll bar appear.

    private const double MinNameWidth = 80;
    private const double RowChrome = 10;  // borders and item padding around the columns
    private bool _otherColumnsWatched;

    private void FileList_SizeChanged(object sender, SizeChangedEventArgs e) => FitNameColumn();

    private void FileList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // The vertical scroll bar coming or going changes the room for the columns.
        if (e.ViewportWidthChange != 0 || e.ExtentHeightChange != 0) FitNameColumn();
    }

    private void FitNameColumn()
    {
        if (FileList.View is not GridView view) return;
        if (!_otherColumnsWatched)
        {
            // Widening another column by hand takes the room from the name column.
            var width = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
            foreach (var column in view.Columns.Where(c => c != NameColumn)) width.AddValueChanged(column, (_, _) => FitNameColumn());
            _otherColumnsWatched = true;
        }
        var scroller = FindChild<ScrollViewer>(FileList);
        var room = (scroller?.ActualWidth ?? FileList.ActualWidth) - RowChrome;
        if (scroller?.ComputedVerticalScrollBarVisibility == Visibility.Visible) room -= SystemParameters.VerticalScrollBarWidth;
        var others = view.Columns.Where(c => c != NameColumn).Sum(c => double.IsNaN(c.Width) ? c.ActualWidth : c.Width);
        var name = Math.Max(MinNameWidth, Math.Floor(room - others));
        if (Math.Abs(NameColumn.Width - name) >= 1) NameColumn.Width = name;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private Task GoToPage(int index) =>
        !_imageClosed && index >= _spread.First && index <= _spread.Last ? Task.CompletedTask : MoveAsync(nav => nav.At(index));

    private async void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await OpenAsync(AddressBox.Text.Trim().Trim('"'));
        Viewer.Focus();
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) await OpenAsync(files[0]);
    }

    // ---- Menus ------------------------------------------------------------------------------

    private async Task ShowOpenDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "書庫と画像|*.zip;*.cbz;*.rar;*.cbr;*.7z;*.cb7;*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.avif;*.jxl|すべてのファイル|*.*",
        };
        if (dialog.ShowDialog(this) == true) await OpenAsync(dialog.FileName);
    }

    private async void Open_Click(object sender, RoutedEventArgs e) => await ShowOpenDialog();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void CloseImage_Click(object sender, RoutedEventArgs e) => CloseImage();

    /// <summary>
    /// Closes the image only, as Leeyes does on Esc: the view goes blank and the pages that were shown
    /// stay selected in the file list, which gets the focus. The book stays open; Enter or a click in the
    /// list (or any page move) shows an image again.
    /// </summary>
    private void CloseImage()
    {
        if (_cache is null || _imageClosed) return;
        _imageClosed = true;
        Viewer.Pages = [];
        Title = "Leaflet - " + _cache.Book.Location;
        var shown = _items.Skip(_spread.First).Take(_spread.Count).ToList();
        FileList.SelectedItems.Clear();
        foreach (var item in shown)
        {
            item.IsShown = false;
            FileList.SelectedItems.Add(item);
        }
        StatusText.Text = $"{shown.Count} 個のオブジェクトを選択";
        if (shown.Count == 0) return;
        FileList.ScrollIntoView(shown[0]);
        FileList.UpdateLayout();
        if (FileList.ItemContainerGenerator.ContainerFromItem(shown[0]) is ListViewItem container) container.Focus();
        else FileList.Focus();
    }

    /// <summary>
    /// Closes the open book altogether: the image, the file list and the cache. The address shows
    /// <paramref name="location"/>, the folder picked in the tree.
    /// </summary>
    private void CloseBook(string location)
    {
        if (_cache is null) return;
        _cache.Dispose();
        _cache = null;
        _items = [];
        FileList.ItemsSource = null;
        _spread = default;
        _imageClosed = false;
        Viewer.Pages = [];
        Title = "Leaflet";
        AddressBox.Text = location;
        SizeStatus.Text = "";
        PageStatus.Text = "";
        StatusText.Text = "";
    }

    private async void NextSpread_Click(object sender, RoutedEventArgs e) => await NextSpread();

    private async void PreviousSpread_Click(object sender, RoutedEventArgs e) => await PreviousSpread();

    private async void StepForward_Click(object sender, RoutedEventArgs e) => await StepForward();

    private async void StepBack_Click(object sender, RoutedEventArgs e) => await StepBack();

    private async void FirstPage_Click(object sender, RoutedEventArgs e) => await FirstPage();

    private async void LastPage_Click(object sender, RoutedEventArgs e) => await LastPage();

    private async void NextBook_Click(object sender, RoutedEventArgs e) => await NextBook();

    private async void PreviousBook_Click(object sender, RoutedEventArgs e) => await PreviousBook();

    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void RecentMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != RecentMenu) return;
        RecentMenu.Items.Clear();
        foreach (var recent in _settings.Recent)
        {
            var location = recent.Location;
            RecentMenu.Items.Add(Item(MenuText(location), false, () => _ = OpenAsync(location)));
        }
        if (RecentMenu.Items.Count == 0) RecentMenu.Items.Add(new MenuItem { Header = "(なし)", IsEnabled = false });
    }

    private void BookmarkMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != BookmarkMenu) return;
        var items = BookmarkMenu.Items;
        items.Clear();
        var add = Item("ブックマークに追加", false, AddBookmark, "Ctrl+D");
        add.IsEnabled = _cache is not null;
        items.Add(add);
        var remove = new MenuItem { Header = "ブックマークを削除", IsEnabled = _settings.Bookmarks.Count > 0 };
        items.Add(remove);
        if (_settings.Bookmarks.Count > 0) items.Add(new Separator());
        foreach (var mark in _settings.Bookmarks)
        {
            var text = $"{MenuText(mark.Location)}  -  {Path.GetFileName(mark.PageName).Replace("_", "__")}";
            items.Add(Item(text, false, () => _ = OpenAsync(mark.Location, pageName: mark.PageName)));
            remove.Items.Add(Item(text, false, () => _settings.Bookmarks.Remove(mark)));
        }
    }

    private void AddBookmark()
    {
        if (_cache is null || _items.Count == 0) return;
        var page = _items[_spread.First].Entry.Name;
        _settings.AddBookmark(_cache.Book.Location, _spread.First, page, DateTime.Now);
        StatusText.Text = $"ブックマークに追加しました: {Path.GetFileName(_cache.Book.Location)} - {Path.GetFileName(page)}";
    }

    /// <summary>A path shortened for a menu: the last two parts. Underscores are doubled so they are not access keys.</summary>
    private static string MenuText(string location)
    {
        var parts = location.TrimEnd('\\').Split('\\');
        var text = parts.Length >= 2 ? $"{parts[^1]}  ({parts[^2]})" : location;
        return text.Replace("_", "__");
    }

    private void ImageMenuRoot_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource == ImageMenuRoot) FillImageMenu(ImageMenuRoot.Items);
    }

    private void Viewer_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = Viewer.ContextMenu;
        FillImageMenu(menu.Items);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(_isFullScreen ? "全画面表示を終了" : "全画面表示", _isFullScreen, ToggleFullScreen, "Enter"));
    }

    /// <summary>The image menu, shared by the menu bar and the right-click menu.</summary>
    private void FillImageMenu(ItemCollection items)
    {
        items.Clear();
        items.Add(Item("自動判定", _mode == PageMode.Auto, () => SetMode(PageMode.Auto)));
        items.Add(Item("単ページ", _mode == PageMode.Single, () => SetMode(PageMode.Single)));
        items.Add(Item("見開き", _mode == PageMode.Spread, () => SetMode(PageMode.Spread)));
        items.Add(Item("表紙を単ページにする", _coverSingle, () =>
        {
            _coverSingle = !_coverSingle;
            _ = Relayout();
        }));
        items.Add(new Separator());
        items.Add(Item("右綴じ", _layout.Binding == Binding.Right, () => SetLayout(_layout with { Binding = Binding.Right })));
        items.Add(Item("左綴じ", _layout.Binding == Binding.Left, () => SetLayout(_layout with { Binding = Binding.Left })));
        items.Add(new Separator());
        items.Add(Item("ウィンドウに合わせる", _layout.Fit == FitMode.Window, () => SetLayout(_layout with { Fit = FitMode.Window })));
        items.Add(Item("幅に合わせる", _layout.Fit == FitMode.Width, () => SetLayout(_layout with { Fit = FitMode.Width })));
        items.Add(Item("高さに合わせる", _layout.Fit == FitMode.Height, () => SetLayout(_layout with { Fit = FitMode.Height })));
        items.Add(Item("原寸", _layout.Fit == FitMode.Original, () => SetLayout(_layout with { Fit = FitMode.Original })));
        items.Add(Item("拡大はしない", _layout.NoEnlarge, () => SetLayout(_layout with { NoEnlarge = !_layout.NoEnlarge })));
        items.Add(Item("2 枚の高さを揃える", _layout.EqualHeight, () => SetLayout(_layout with { EqualHeight = !_layout.EqualHeight })));
        items.Add(new Separator());
        var scaling = new MenuItem { Header = "補間" };
        foreach (var (name, mode) in new[]
                 {
                     ("高品質", BitmapScalingMode.HighQuality),
                     ("標準", BitmapScalingMode.Linear),
                     ("ニアレストネイバー", BitmapScalingMode.NearestNeighbor),
                 })
        {
            scaling.Items.Add(Item(name, Viewer.ScalingMode == mode, () => Viewer.ScalingMode = mode));
        }
        items.Add(scaling);
    }

    private static MenuItem Item(string header, bool isChecked, Action action, string? gesture = null)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetMode(PageMode mode)
    {
        _mode = mode;
        _ = Relayout();
    }

    private void SetLayout(LayoutOptions layout)
    {
        _layout = layout;
        Viewer.Options = layout;
    }

    private void ToggleFullScreen()
    {
        _isFullScreen = !_isFullScreen;
        var visibility = _isFullScreen ? Visibility.Collapsed : Visibility.Visible;
        if (_isFullScreen)
        {
            _leftWidth = LeftColumn.Width;
            _restoreState = WindowState;
            LeftColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            WindowStyle = WindowStyle.None;
            // Re-maximize so that the borderless window covers the taskbar.
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            LeftColumn.Width = _leftWidth;
            SplitterColumn.Width = new GridLength(4);
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _restoreState;
        }
        LeftPane.Visibility = visibility;
        MainSplitter.Visibility = visibility;
        AddressPanel.Visibility = visibility;
        StatusPanel.Visibility = visibility;
        PlaceMenu(overlay: _isFullScreen);
        Viewer.Focus();
    }

    // ---- Menu in full screen ----------------------------------------------------------------
    //
    // In full screen the menu lies over the top of the image, so showing and hiding it does not move
    // the image. It hides after a few seconds and comes back on mouse moves, clicks and keys other than
    // page turning.

    private static readonly TimeSpan MenuHideDelay = TimeSpan.FromSeconds(3);
    private DispatcherTimer? _menuTimer;
    private Point? _lastMousePosition;

    private void PlaceMenu(bool overlay)
    {
        if (overlay == (MainMenu.Parent == RootGrid)) return;
        ((Panel)MainMenu.Parent).Children.Remove(MainMenu);
        if (overlay)
        {
            MainMenu.VerticalAlignment = VerticalAlignment.Top;
            Panel.SetZIndex(MainMenu, 1);
            RootGrid.Children.Add(MainMenu);
            ShowMenuForAWhile();
        }
        else
        {
            _menuTimer?.Stop();
            MainMenu.Visibility = Visibility.Visible;
            RootDock.Children.Insert(0, MainMenu);
        }
    }

    /// <summary>In full screen: shows the menu and hides it again after <see cref="MenuHideDelay"/>.</summary>
    private void ShowMenuForAWhile()
    {
        if (!_isFullScreen) return;
        MainMenu.Visibility = Visibility.Visible;
        if (_menuTimer is null)
        {
            _menuTimer = new DispatcherTimer { Interval = MenuHideDelay };
            _menuTimer.Tick += (_, _) =>
            {
                // Keep it while it is in use: a submenu open, the keyboard in it, or the mouse over it.
                if (MainMenu.IsKeyboardFocusWithin || MainMenu.IsMouseOver
                    || MainMenu.Items.OfType<MenuItem>().Any(i => i.IsSubmenuOpen))
                {
                    return;
                }
                _menuTimer.Stop();
                if (_isFullScreen) MainMenu.Visibility = Visibility.Collapsed;
            };
        }
        _menuTimer.Stop();
        _menuTimer.Start();
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isFullScreen) return;
        // WPF also reports a "move" when the layout changes under a still mouse: only real moves count.
        var position = PointToScreen(e.GetPosition(this));
        if (_lastMousePosition is { } last && Math.Abs(position.X - last.X) + Math.Abs(position.Y - last.Y) < 4) return;
        _lastMousePosition = position;
        ShowMenuForAWhile();
    }

    /// <summary>Keys that turn pages (or modifiers on their own) leave the menu hidden.</summary>
    private static bool IsPageKey(Key key) => key is Key.Up or Key.Down or Key.Left or Key.Right
        or Key.PageUp or Key.PageDown or Key.Home or Key.End
        or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift;
}
