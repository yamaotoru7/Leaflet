using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
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
/// </summary>
public sealed class TreeNode : INotifyPropertyChanged
{
    private static readonly TreeNode Placeholder = new("…", "", NodeKind.Folder, null, null);

    private bool _isExpanded;
    private bool _isSelected;
    private Task? _loading;

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
            if (value) _ = LoadChildrenAsync();
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
        Children.Clear();
        foreach (var child in children) Children.Add(child);
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
