using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Leaflet.Core.Updates;

namespace Leaflet.Core.Tests;

public sealed class UpdaterTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v0.2.10", "draft": false, "prerelease": false,
          "html_url": "https://github.com/yamaotoru7/Leaflet/releases/tag/v0.2.10",
          "body": "Leaflet 0.2.10。\r\n\r\n- **修正**：x\r\n",
          "assets": [
            { "name": "Leaflet-0.2.10-setup.exe", "browser_download_url": "https://example.test/setup.exe",
              "url": "https://api.example.test/assets/1" },
            { "name": "Leaflet-0.2.10-setup.exe.sha256", "browser_download_url": "https://example.test/setup.exe.sha256",
              "url": "https://api.example.test/assets/2" }
          ]
        }
        """;

    [Fact]
    public void ParsesAGitHubRelease()
    {
        var release = Updater.Parse(Json(ReleaseJson));

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 2, 10), release.Version);
        Assert.Equal("Leaflet-0.2.10-setup.exe", release.InstallerName);
        Assert.Equal("https://example.test/setup.exe", release.InstallerUrl);
        Assert.Equal("https://example.test/setup.exe.sha256", release.ChecksumUrl);
        Assert.Equal("https://api.example.test/assets/1", release.InstallerApiUrl);
        Assert.Equal("https://api.example.test/assets/2", release.ChecksumApiUrl);
        Assert.Equal("Leaflet 0.2.10。\n\n- **修正**：x", release.Notes);
    }

    [Fact]
    public void ComparesVersionsAsNumbers()
    {
        var release = Updater.Parse(Json(ReleaseJson))!;

        Assert.True(Updater.IsNewer(release, new Version(0, 2, 9)));
        Assert.False(Updater.IsNewer(release, new Version(0, 2, 10)));
        Assert.False(Updater.IsNewer(release, new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData(".exe.sha256\"", ".exe.txt\"")]                                   // no .sha256
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]                 // pre-release
    [InlineData("\"v0.2.10\"", "\"latest\"")]                                     // unreadable tag
    [InlineData("\"Leaflet-0.2.10-setup.exe", "\"..\\\\..\\\\Leaflet-0.2.10-setup.exe")] // a path as the asset name
    public void SkipsReleasesThatCannotBeInstalled(string from, string to)
    {
        Assert.Null(Updater.Parse(Json(ReleaseJson.Replace(from, to))));
    }

    [Fact]
    public void ChecksOncePerDay()
    {
        var now = DateTimeOffset.Now;
        Assert.True(Updater.IsDue(null, now));
        Assert.False(Updater.IsDue(now.AddHours(-23), now));
        Assert.True(Updater.IsDue(now.AddHours(-25), now));
    }

    /// <summary>Updating from 0.2.4 to 0.2.7 shows the notes of 0.2.7, 0.2.6 and 0.2.5, newest first.</summary>
    [Fact]
    public void CombinesTheNotesOfSkippedVersions()
    {
        var list = Json("""
            [
              { "tag_name": "v0.2.7", "draft": false, "prerelease": false, "body": "Leaflet 0.2.7。\r\n\r\n- **c**：7" },
              { "tag_name": "v0.2.6", "draft": false, "prerelease": false, "body": "Leaflet 0.2.6。\r\n\r\n- **b**：6" },
              { "tag_name": "v0.2.6-rc", "draft": false, "prerelease": true, "body": "rc" },
              { "tag_name": "v0.2.8", "draft": true, "prerelease": false, "body": "draft" },
              { "tag_name": "v0.2.5", "draft": false, "prerelease": false, "body": "- **a**：5" },
              { "tag_name": "v0.2.4", "draft": false, "prerelease": false, "body": "Leaflet 0.2.4。\n\n- old" }
            ]
            """);
        var notes = list.EnumerateArray().Select(Updater.ParseNotes).OfType<(Version, string)>().ToList();
        Assert.Equal(4, notes.Count);
        var latest = new ReleaseInfo(new Version(0, 2, 7), "v0.2.7", "Leaflet 0.2.7。\n\n- **c**：7", "", "x-setup.exe", "", "");

        Assert.Equal("## 0.2.7\n\n- **c**：7\n\n## 0.2.6\n\n- **b**：6\n\n## 0.2.5\n\n- **a**：5",
            Updater.CombineNotes(notes, latest, new Version(0, 2, 4)));
        // Only the latest is new: its notes stay as published.
        Assert.Equal(latest.Notes, Updater.CombineNotes(notes, latest, new Version(0, 2, 6)));
    }

    /// <summary>Against a local HTTP server: a complete download, the API fallback, a stalled body and a wrong checksum.</summary>
    [Fact]
    public async Task DownloadsVerifiesAndGivesUpOnAStalledBody()
    {
        var installer = new byte[200_000];
        new Random(8).NextBytes(installer);
        var checksum = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(installer)) + "  installer\n");
        // Like GitHub's API, the asset URLs under /api/ return metadata unless the file is asked for.
        var metadata = Encoding.ASCII.GetBytes("{\"name\": \"asset\"}");
        using var server = new TestHttpServer((path, accept) => path switch
        {
            "/ok-setup.exe" or "/bad-setup.exe" => (200, installer, false),
            "/stall-setup.exe" => (200, installer, true),
            "/bad-setup.exe.sha256" => (200, Encoding.ASCII.GetBytes(new string('0', 64) + "  bad-setup.exe\n"), false),
            // The github.com download URL failing as it did for anonymous clients.
            "/gone-setup.exe" or "/gone-setup.exe.sha256" => (504, Encoding.ASCII.GetBytes("504 Gateway Time-out"), false),
            "/api/gone-setup.exe" => (200, accept == "application/octet-stream" ? installer : metadata, false),
            "/api/gone-setup.exe.sha256" => (200, accept == "application/octet-stream" ? checksum : metadata, false),
            _ => (200, checksum, false),
        });
        ReleaseInfo Release(string name) => new(new Version(9, 9, 9), "v9.9.9", "", "", name,
            server.Url + name, server.Url + name + ".sha256", server.Url + "api/" + name, server.Url + "api/" + name + ".sha256");
        var progress = new Progress<double>(_ => { });
        var previous = Updater.StallTimeout;
        Updater.StallTimeout = TimeSpan.FromSeconds(1);
        var paths = new List<string>();
        var before = Directory.Exists(Updater.DownloadRoot) ? Directory.GetDirectories(Updater.DownloadRoot).ToHashSet() : [];
        try
        {
            var path = await Updater.DownloadAsync(Release("ok-setup.exe"), progress, CancellationToken.None);
            paths.Add(path);
            Assert.Equal(installer, await File.ReadAllBytesAsync(path));

            // The download URL answers 504: both files come through the API instead.
            var fallback = await Updater.DownloadAsync(Release("gone-setup.exe"), progress, CancellationToken.None);
            paths.Add(fallback);
            Assert.Equal(installer, await File.ReadAllBytesAsync(fallback));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<IOException>(() => Updater.DownloadAsync(Release("stall-setup.exe"), progress, CancellationToken.None));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"stalled download gave up after {watch.Elapsed.TotalSeconds:F1} s");

            await Assert.ThrowsAsync<InvalidDataException>(() => Updater.DownloadAsync(Release("bad-setup.exe"), progress, CancellationToken.None));

            // Failed downloads leave nothing behind; the good ones stay in folders of their own.
            var added = Directory.GetDirectories(Updater.DownloadRoot).Where(d => !before.Contains(d)).ToArray();
            Assert.Equal(2, added.Length);
            Assert.Contains(Path.GetDirectoryName(path), added);
            Assert.Contains(Path.GetDirectoryName(fallback), added);
        }
        finally
        {
            Updater.StallTimeout = previous;
            foreach (var path in paths) Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>
    /// Serves each request with Connection: close. A stalled response sends the headers and
    /// a tenth of the body, then holds the connection open.
    /// </summary>
    private sealed class TestHttpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        /// <param name="respond">Gets the path and the Accept header.</param>
        public TestHttpServer(Func<string, string, (int Status, byte[] Body, bool Stall)> respond)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch (OperationCanceledException) { return; }
                    catch (SocketException) { return; }
                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            try
                            {
                                var stream = client.GetStream();
                                var reader = new StreamReader(stream, Encoding.ASCII);
                                var path = (await reader.ReadLineAsync())?.Split(' ')[1] ?? "/";
                                var accept = "";
                                for (string? line; !string.IsNullOrEmpty(line = await reader.ReadLineAsync());)
                                {
                                    if (line.StartsWith("Accept:", StringComparison.OrdinalIgnoreCase)) accept = line["Accept:".Length..].Trim();
                                }
                                var (status, body, stall) = respond(path, accept);
                                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                                await stream.WriteAsync(head);
                                await stream.WriteAsync(stall ? body.AsMemory(0, body.Length / 10) : body);
                                if (stall) await Task.Delay(Timeout.Infinite, _stop.Token);
                            }
                            catch (Exception error) when (error is IOException or OperationCanceledException) { }
                        }
                    });
                }
            });
        }

        public string Url { get; }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
