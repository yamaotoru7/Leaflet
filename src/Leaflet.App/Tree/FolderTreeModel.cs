using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Leaflet.App.Tree;

/// <summary>
/// The roots of the folder tree (Desktop, Downloads, Pictures and Documents, then the drives, and network
/// shares opened by path) and finding a path in it.
/// </summary>
public sealed partial class FolderTreeModel
{
    /// <summary>Known folders shown above the drives, in this order.</summary>
    private static readonly Guid[] KnownFolders =
    [
        new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), // Desktop
        new("374DE290-123F-4565-9164-39C4925E467B"), // Downloads
        new("33E28130-4E1E-4676-835A-98395C3BC3BB"), // Pictures
        new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), // Documents
    ];

    public ObservableCollection<TreeNode> Roots { get; } = [];

    /// <summary>The known folders come first in Roots; the drives follow them.</summary>
    private int _knownCount;

    /// <summary>The drive letters the tree shows, to tell cheaply whether they have changed.</summary>
    private string[] _driveNames = [];

    private Task? _refreshingDrives;
    private bool _drivesPending;

    /// <summary>Lists the drives. Names are read in the background because a disconnected network drive can be slow.</summary>
    public async Task LoadDrivesAsync()
    {
        // Known folders by their real location (it may have been moved, e.g. to OneDrive) and Explorer's name.
        var folders = await Task.Run(() => KnownFolders
            .Select(KnownFolderPath)
            .OfType<string>()
            .Where(Directory.Exists)
            .Select(p => (Path: p, Display: ShellInfo.DisplayName(p), Icon: ShellInfo.IconOf(p)))
            .ToList());
        foreach (var (path, display, icon) in folders)
        {
            Roots.Insert(_knownCount++, new TreeNode(display, path, NodeKind.Folder, null, icon));
        }
        await RefreshDrivesAsync();
    }

    /// <summary>Whether drives have been added or removed since the tree last listed them (cheap).</summary>
    public bool DrivesChanged()
    {
        try
        {
            return !Environment.GetLogicalDrives().SequenceEqual(_driveNames, StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Brings the drives in the tree in line with the drives there are now. A drive whose name has changed
    /// (other media in the same drive) is replaced; the others keep what is expanded under them.
    /// Calls made while a listing runs are folded into one more listing after it.
    /// </summary>
    public Task RefreshDrivesAsync()
    {
        if (_refreshingDrives is { IsCompleted: false })
        {
            _drivesPending = true;
            return _refreshingDrives;
        }
        return _refreshingDrives = RefreshDrivesLoopAsync();
    }

    private async Task RefreshDrivesLoopAsync()
    {
        do
        {
            _drivesPending = false;
            await ListDrivesAsync();
        }
        while (_drivesPending);
    }

    private async Task ListDrivesAsync()
    {
        var drives = await Task.Run(() => DriveInfo.GetDrives()
            .Select(d => (d.Name, Display: ShellInfo.DisplayName(d.Name), Icon: ShellInfo.IconOf(d.Name)))
            .ToList());
        _driveNames = drives.Select(d => d.Name).ToArray();

        var existing = Roots.Where(r => r.Kind == NodeKind.Drive).ToList();
        var wanted = drives
            .Select(d => existing.FirstOrDefault(r => string.Equals(r.Path, d.Name, StringComparison.OrdinalIgnoreCase) && r.Name == d.Display)
                ?? new TreeNode(d.Display, d.Name, NodeKind.Drive, null, d.Icon))
            .ToList();
        foreach (var gone in existing.Except(wanted))
        {
            Roots.Remove(gone);
            gone.Detach();
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var at = Roots.IndexOf(wanted[i]);
            if (at < 0) Roots.Insert(_knownCount + i, wanted[i]);
            else if (at != _knownCount + i) Roots.Move(at, _knownCount + i);
        }
    }

    private static string? KnownFolderPath(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, 0, out var pointer) != 0) return null;
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);

    /// <summary>Whether <paramref name="location"/> is <paramref name="root"/> or inside it.</summary>
    private static bool IsUnder(string location, string root)
    {
        root = root.TrimEnd('\\');
        return location.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && (location.Length == root.Length || location[root.Length] == '\\');
    }

    /// <summary>
    /// Expands the tree down to <paramref name="location"/> and returns the deepest node reached
    /// (the node itself when the whole path was found), or null when no root matches.
    /// </summary>
    public async Task<TreeNode?> RevealAsync(string location)
    {
        location = location.TrimEnd('\\');
        // The closest root wins: a book on the desktop is shown under Desktop, not under C:.
        var root = Roots.Where(r => IsUnder(location, r.Path)).MaxBy(r => r.Path.TrimEnd('\\').Length);
        if (root is null && location.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // A network share opened by its UNC path gets its own root, as \\server\share.
            var parts = location[2..].Split('\\');
            if (parts.Length < 2) return null;
            var share = $@"\\{parts[0]}\{parts[1]}";
            root = new TreeNode(share, share, NodeKind.Folder, null, ShellInfo.TypeIcon(folder: true));
            Roots.Add(root);
        }
        if (root is null) return null;

        var node = root;
        var rest = location[root.Path.TrimEnd('\\').Length..].Trim('\\');
        foreach (var segment in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            node.IsExpanded = true;
            await node.LoadChildrenAsync();
            var child = node.Children.FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (child is null) break;
            node = child;
        }
        return node;
    }
}
