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
            Roots.Add(new TreeNode(display, path, NodeKind.Folder, null, icon));
        }

        var drives = await Task.Run(() => DriveInfo.GetDrives()
            .Select(d => (d.Name, Display: ShellInfo.DisplayName(d.Name), Icon: ShellInfo.IconOf(d.Name)))
            .ToList());
        foreach (var (path, display, icon) in drives)
        {
            Roots.Add(new TreeNode(display, path, NodeKind.Drive, null, icon));
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
