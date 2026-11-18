using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using Leaflet.Core.Updates;

namespace Leaflet.App.Updates;

/// <summary>Shows the release notes. "更新して再起動" downloads and verifies the installer; DialogResult is true when it is ready to start.</summary>
public partial class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private CancellationTokenSource? _download;

    internal string? InstallerPath { get; private set; }

    internal UpdateWindow(ReleaseInfo release)
    {
        _release = release;
        InitializeComponent();
        Headline.Text = $"Leaflet {release.Version} があります";
        Subline.Text = $"現在の版は {Updater.CurrentVersion} です。";
        var notes = MarkdownDocument.Build(release.Notes.Length > 0 ? release.Notes : "（リリースノートはありません）",
            SystemColors.HotTrackBrush, new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)));
        notes.FontFamily = FontFamily;
        notes.FontSize = FontSize;
        notes.LineHeight = 20;
        Notes.Document = notes;
    }

    private async void InstallClick(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        _download = new CancellationTokenSource();
        try
        {
            ProgressText.Text = $"{_release.InstallerName} をダウンロードしています…";
            var progress = new Progress<double>(p => ProgressText.Text = $"{_release.InstallerName} をダウンロードしています… {p:P0}");
            var installer = await Updater.DownloadAsync(_release, progress, _download.Token);
            // Closed while the last step finished: DialogResult can no longer be set.
            if (_download.IsCancellationRequested) return;
            InstallerPath = installer;
            DialogResult = true;
        }
        catch (OperationCanceledException) when (_download.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            Trace.TraceError(error.ToString());
            ProgressText.Text = $"ダウンロードできませんでした。 {error.Message}";
            InstallButton.IsEnabled = true;
        }
        finally
        {
            _download?.Dispose();
            _download = null;
        }
    }

    private void LaterClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _download?.Cancel();
        base.OnClosing(e);
    }

    private void OpenPage(object sender, RoutedEventArgs e)
    {
        var url = _release.PageUrl.Length > 0 ? _release.PageUrl : Updater.ReleasesPage;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            ProgressText.Text = $"ブラウザーを開けませんでした。 {url}";
            Trace.TraceError(error.ToString());
        }
    }
}
