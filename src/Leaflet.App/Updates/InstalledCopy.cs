using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Leaflet.App.Updates;

/// <summary>The copy the installer put in place, and starting a downloaded installer over it.</summary>
internal static class InstalledCopy
{
    /// <summary>Inno Setup writes the uninstall entry as "&lt;AppId&gt;_is1" (AppId in installer/Leaflet.iss; never change either).</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{92262E79-CF64-4553-9D60-24CA2D62301B}_is1";

    /// <summary>True only for the installed copy (not bin\Debug or a copied folder).</summary>
    public static bool IsThisOne
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
                return key?.GetValue("InstallLocation") is string dir && string.Equals(
                    Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    /// <summary>Installs silently and relaunches the app afterwards (/RELAUNCH=1 in Leaflet.iss). The caller closes the app.</summary>
    public static void StartInstaller(string installer, string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var log = Path.Combine(logDirectory, "update-setup.log");
        using var _ = Process.Start(new ProcessStartInfo(installer,
            $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1 /LOG=\"{log}\"") { UseShellExecute = false });
    }
}
