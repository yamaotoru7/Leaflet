using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using Leaflet.Core.Books;

namespace Leaflet.App.Tree;

public enum NodeKind
{
    Drive,
    Folder,
    Archive,
    ArchiveFolder,
}

/// <summary>
/// One node of the folder tree. Children are listed in the background the first time the node is expanded,
/// so a slow network folder does not freeze the window. As in Leeyes, archives are nodes that open like folders.
/// While a folder node is expanded its folder is watched, and its children follow what is on disk;
/// expanding it again also lists it again.
/// </summary>
public sealed class TreeNode : INotifyPropertyChanged
{
    private static readonly TreeNode Placeholder = new("…", "", NodeKind.Folder, null, null);

    private bool _isExpanded;
    private bool _isSelected;
    private Task? _loading;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _refreshTimer;

    public TreeNode(string name, string path, NodeKind kind, TreeNode? parent, ImageSource? icon)
    {
        Name = name;
        Path = path;
        Kind = kind;
        Parent = parent;
        Icon = icon;
        // Leeyes does not check for subfolders before expanding: every folder shows an expander.
        if (path.Length > 0) Children.Add(Placeholder);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    /// <summary>The location this node opens: a folder, an archive, or a folder inside an archive.</summary>
    public string Path { get; }

    public NodeKind Kind { get; }

    public TreeNode? Parent { get; }

    public ImageSource? Icon { get; }

    public ObservableCollection<TreeNode> Children { get; } = [];

    /// <summary>For an archive folder: its path inside the archive ("a\b").</summary>
    public string InnerPath { get; init; } = "";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            if (value)
            {
                // Already listed: list again, the folder may have changed while it was not watched.
                if (_loading is not null) _ = RefreshAsync();
                else _ = LoadChildrenAsync();
                StartWatching();
            }
            else
            {
                StopWatching();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Lists the children once. Later calls return the same task.</summary>
    public Task LoadChildrenAsync() => _loading ??= LoadAsync();

    /// <summary>
    /// Lists a folder again and brings the children in line with it, keeping the nodes that are still there
    /// (with their expansion and selection). Archives are listed only once.
    /// </summary>
    public Task RefreshAsync()
    {
        if (_loading is null) return LoadChildrenAsync();
        if (Kind is not (NodeKind.Drive or NodeKind.Folder)) return _loading;
        return _loading = RefreshAfterAsync(_loading);
    }

    private async Task RefreshAfterAsync(Task previous)
    {
        await previous;
        await LoadAsync();
    }

    /// <summary>For an archive node: the inner folders, filled in by the archive node itself.</summary>
    internal List<(string Inner, TreeNode Node)>? ArchiveFolders { get; private set; }

    private async Task LoadAsync()
    {
        List<TreeNode> children;
        try
        {
            children = Kind switch
            {
                NodeKind.Drive or NodeKind.Folder => await Task.Run(ListFolder),
                NodeKind.Archive => await Task.Run(ListArchive),
                _ => ArchiveChildren(),
            };
        }
        catch (Exception)
        {
            // An unreadable folder or a broken archive just has no children.
            children = [];
        }
        Merge(children);
    }

    /// <summary>
    /// Makes Children equal to <paramref name="listed"/>, reusing the nodes already there for the same path,
    /// so the tree keeps what is expanded and selected under them.
    /// </summary>
    private void Merge(List<TreeNode> listed)
    {
        var existing = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in Children)
        {
            if (child.Path.Length > 0) existing.TryAdd(child.Kind + "|" + child.Path, child);
        }
        var wanted = listed.Select(n => existing.GetValueOrDefault(n.Kind + "|" + n.Path) ?? n).ToList();

        for (var i = 0; i < wanted.Count; i++)
        {
            var node = wanted[i];
            if (i < Children.Count && ReferenceEquals(Children[i], node)) continue;
            var at = Children.IndexOf(node);
            if (at > i) Children.Move(at, i);
            else Children.Insert(i, node);
        }
        while (Children.Count > wanted.Count)
        {
            var gone = Children[^1];
            Children.RemoveAt(Children.Count - 1);
            gone.Detach();
        }
    }

    /// <summary>Whether the children have been listed (or are being listed).</summary>
    internal bool IsLoaded => _loading is not null;

    /// <summary>Lists this node again, then every expanded node under it (F5).</summary>
    public async Task RefreshExpandedAsync()
    {
        if (!IsLoaded) return;
        await RefreshAsync();
        foreach (var child in Children.ToList())
        {
            if (child.IsExpanded && child.IsLoaded) await child.RefreshExpandedAsync();
        }
    }

    /// <summary>Stops watching this node and everything under it (it has left the tree).</summary>
    internal void Detach()
    {
        StopWatching();
        foreach (var child in Children)
        {
            if (!ReferenceEquals(child, Placeholder)) child.Detach();
        }
    }

    private void StartWatching()
    {
        if (_watcher is not null || Kind is not (NodeKind.Drive or NodeKind.Folder) || Path.Length == 0) return;
        try
        {
            var watcher = new FileSystemWatcher(Path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
            };
            watcher.Created += OnFolderChanged;
            watcher.Deleted += OnFolderChanged;
            watcher.Renamed += OnFolderChanged;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // A folder that cannot be watched (gone, a share that does not allow it) is listed on expanding only.
        }
    }

    private void StopWatching()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
        _refreshTimer?.Stop();
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e) => ScheduleRefresh(sender);

    private void OnWatcherError(object sender, ErrorEventArgs e) => ScheduleRefresh(sender);

    /// <summary>Events come on a pool thread and in bursts (a copy of many files): list again once they settle.</summary>
    private void ScheduleRefresh(object watcher)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(() =>
        {
            if (!ReferenceEquals(watcher, _watcher)) return;  // stopped meanwhile
            if (_refreshTimer is null)
            {
                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _refreshTimer.Tick += (_, _) =>
                {
                    _refreshTimer.Stop();
                    if (_watcher is not null) _ = RefreshAsync();
                };
            }
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private List<TreeNode> ListFolder()
    {
        var dir = new DirectoryInfo(Path);
        return dir.EnumerateFileSystemInfos()
            .Where(i => i is DirectoryInfo d ? !d.Attributes.HasFlag(FileAttributes.System) : FileTypes.IsArchive(i.Name))
            .OrderBy(i => i.Name, NaturalComparer.Instance)
            .Select(i => i is DirectoryInfo
                ? new TreeNode(i.Name, i.FullName, NodeKind.Folder, this, ShellInfo.TypeIcon(folder: true))
                : new TreeNode(i.Name, i.FullName, NodeKind.Archive, this, ShellInfo.TypeIcon(folder: false, i.Extension)))
            .ToList();
    }

    private List<TreeNode> ListArchive()
    {
        // Build the whole inner folder tree at once; deeper levels are handed out by ArchiveChildren.
        var folders = ArchiveBook.ListImageFolders(Path);
        var nodes = new List<(string Inner, TreeNode Node)>();
        var byInner = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var inner in folders)
        {
            var cut = inner.LastIndexOf('\\');
            var parent = cut < 0 ? this : byInner[inner[..cut]];
            var node = new TreeNode(inner[(cut + 1)..], Path + "\\" + inner, NodeKind.ArchiveFolder, parent, ShellInfo.TypeIcon(folder: true))
            {
                InnerPath = inner,
            };
            byInner[inner] = node;
            nodes.Add((inner, node));
        }
        ArchiveFolders = nodes;
        return nodes.Where(n => !n.Inner.Contains('\\')).Select(n => n.Node).ToList();
    }

    private List<TreeNode> ArchiveChildren()
    {
        var archive = Parent;
        while (archive is { Kind: NodeKind.ArchiveFolder }) archive = archive.Parent;
        var prefix = InnerPath + "\\";
        return archive?.ArchiveFolders?
            .Where(n => n.Inner.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !n.Inner[prefix.Length..].Contains('\\'))
            .Select(n => n.Node)
            .ToList() ?? [];
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
