using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Leaflet.App.Tree;

/// <summary>Names and small icons as Explorer shows them.</summary>
internal static partial class ShellInfo
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_DISPLAYNAME = 0x200;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private static readonly Dictionary<string, ImageSource?> TypeIcons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Explorer's name for a drive, e.g. "raid0 (\\192.168.11.111) (R:)".</summary>
    public static string DisplayName(string path)
    {
        var info = new SHFILEINFO();
        return SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_DISPLAYNAME) != 0 && info.szDisplayName.Length > 0
            ? info.szDisplayName
            : path;
    }

    /// <summary>The icon of this particular item (drives). Call on the UI thread or freeze the result.</summary>
    public static ImageSource? IconOf(string path) => Icon(path, 0, 0);

    /// <summary>The icon for a kind of item without touching the disk: a folder, or a file type by extension.</summary>
    public static ImageSource? TypeIcon(bool folder, string extension = "")
    {
        var key = folder ? "<folder>" : extension;
        lock (TypeIcons)
        {
            if (!TypeIcons.TryGetValue(key, out var icon))
            {
                icon = folder
                    ? Icon("folder", FILE_ATTRIBUTE_DIRECTORY, SHGFI_USEFILEATTRIBUTES)
                    : Icon("file" + extension, FILE_ATTRIBUTE_NORMAL, SHGFI_USEFILEATTRIBUTES);
                TypeIcons[key] = icon;
            }
            return icon;
        }
    }

    private static ImageSource? Icon(string path, uint attributes, uint flags)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON | flags) == 0 || info.hIcon == 0)
        {
            return null;
        }
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);
}
