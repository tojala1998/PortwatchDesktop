using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PortwatchDesktop;

internal sealed record AppUpdate(Version Version, string AssetName, Uri DownloadUrl, string Sha256, long Size);
internal sealed record PreparedAppUpdate(AppUpdate Release, string FilePath)
{
    public void Discard() => AppUpdateService.DiscardFile(FilePath);
}

internal sealed class AppUpdateService
{
    private const string Repository = "tojala1998/PortwatchDesktop";
    private const string Installer = "PortwatchDesktop-Setup.exe";
    private const string Portable = "PortwatchDesktop-Portable.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AB7A3E42-9B1A-45E3-B224-4A1C68275F08}_is1";
    private const long MaximumAssetSize = 512L * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly string? executablePath = Environment.ProcessPath;
    private static readonly Version AssemblyVersion = typeof(AppUpdateService).Assembly.GetName().Version!;
    public static Version CurrentVersion { get; } = new(AssemblyVersion.Major, AssemblyVersion.Minor, AssemblyVersion.Build);

    static AppUpdateService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd($"PortwatchDesktop/{CurrentVersion}");

    public async Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        bool installed = IsInstalled(executablePath);
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var release = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ReadRelease(release.RootElement, CurrentVersion, installed);
    }

    internal static AppUpdate? ReadRelease(JsonElement release, Version currentVersion, bool installed)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;

        string? tag = release.GetProperty("tag_name").GetString();
        if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag.AsSpan(1), out var version) ||
            version.Build < 0 || version.Revision != -1 || tag != $"v{version}")
            throw new InvalidDataException("GitHub returned an unsupported release version.");
        if (version <= currentVersion) return null;

        string assetName = installed ? Installer : Portable;
        var url = new Uri($"https://github.com/{Repository}/releases/download/{tag}/{assetName}");
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != assetName) continue;
            string? digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
            long size = asset.GetProperty("size").GetInt64();
            if (asset.GetProperty("state").GetString() == "uploaded" &&
                asset.GetProperty("browser_download_url").GetString() == url.AbsoluteUri &&
                size > 0 && size <= MaximumAssetSize &&
                digest is not null && digest.Length == 71 && digest.StartsWith("sha256:", StringComparison.Ordinal) &&
                digest.Skip(7).All(Uri.IsHexDigit))
                return new(version, assetName, url, digest[7..], size);

            throw new InvalidDataException("GitHub did not provide a verifiable update download.");
        }
        throw new InvalidDataException($"The GitHub release is missing {assetName}.");
    }

    public async Task<PreparedAppUpdate> PrepareAsync(AppUpdate release, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PortwatchDesktop-update-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, release.AssetName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        try
        {
            Directory.CreateDirectory(directory);
            using var response = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new InvalidDataException("The update download has an unexpected size.");

            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                received += read;
                if (received > release.Size) throw new InvalidDataException("The update download exceeded its expected size.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }

            if (received != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update download did not pass its SHA-256 check.");
            return new(release, path);
        }
        catch
        {
            DiscardFile(path);
            throw;
        }
    }

    public void StartInstall(PreparedAppUpdate update, bool restart)
    {
        if (executablePath is null || FileVersionInfo.GetVersionInfo(executablePath).ProductName != "PortwatchDesktop")
            throw new InvalidOperationException("Run PortwatchDesktop.exe to install an update.");
        using var script = typeof(AppUpdateService).Assembly.GetManifestResourceStream("PortwatchDesktop.ApplyUpdate.ps1")
            ?? throw new InvalidOperationException("The update worker is missing.");
        using var reader = new StreamReader(script);
        string command = Convert.ToBase64String(Encoding.Unicode.GetBytes(reader.ReadToEnd()));

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-STA", "-EncodedCommand", command })
            start.ArgumentList.Add(argument);

        using var parent = Process.GetCurrentProcess();
        start.Environment["PORTWATCH_UPDATE_PARENT_ID"] = parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PORTWATCH_UPDATE_PARENT_START"] = parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PORTWATCH_UPDATE_TARGET"] = executablePath;
        start.Environment["PORTWATCH_UPDATE_ASSET"] = update.FilePath;
        start.Environment["PORTWATCH_UPDATE_HASH"] = update.Release.Sha256;
        start.Environment["PORTWATCH_UPDATE_INSTALLER"] = update.Release.AssetName == Installer ? "1" : "0";
        start.Environment["PORTWATCH_UPDATE_RESTART"] = restart ? "1" : "0";
        start.Environment["PORTWATCH_UPDATE_INTERACTIVE"] = "1";
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("Could not start the update worker.");
    }

    private static bool IsInstalled(string? executable)
    {
        if (executable is null || !string.Equals(Path.GetFileName(executable), "PortwatchDesktop.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        string defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PortwatchDesktop");
        using var uninstall = Registry.CurrentUser.OpenSubKey(UninstallKey);
        string? location = uninstall?.GetValue("InstallLocation") as string;
        string installedDirectory = string.IsNullOrWhiteSpace(location) ? defaultDirectory : location;
        return string.Equals(Path.TrimEndingDirectorySeparator(installedDirectory), Path.GetDirectoryName(executable), StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsUpdateError(Exception error) =>
        error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
        KeyNotFoundException or InvalidOperationException or FormatException or Win32Exception or OperationCanceledException;

    internal static void DiscardFile(string path)
    {
        try
        {
            File.Delete(path);
            Directory.Delete(Path.GetDirectoryName(path)!);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void SelfTest()
    {
        using var release = JsonDocument.Parse("""
            {"draft":false,"prerelease":false,"tag_name":"v9.1.2","assets":[
                {"name":"PortwatchDesktop-Setup.exe","state":"uploaded","size":100,
                 "browser_download_url":"https://github.com/tojala1998/PortwatchDesktop/releases/download/v9.1.2/PortwatchDesktop-Setup.exe",
                 "digest":"sha256:0000111122223333444455556666777788889999aaaabbbbccccddddeeeeffff"},
                {"name":"PortwatchDesktop-Portable.exe","state":"uploaded","size":200,
                 "browser_download_url":"https://github.com/tojala1998/PortwatchDesktop/releases/download/v9.1.2/PortwatchDesktop-Portable.exe",
                 "digest":"sha256:0000111122223333444455556666777788889999aaaabbbbccccddddeeeeffff"}
            ]}
            """);
        Version current = new(9, 1, 1);
        var installed = ReadRelease(release.RootElement, current, installed: true);
        var portable = ReadRelease(release.RootElement, current, installed: false);
        if (installed?.AssetName != Installer || portable?.AssetName != Portable ||
            ReadRelease(release.RootElement, new Version(9, 1, 2), installed: false) is not null)
            throw new Exception("Updater release selection failed.");

        using var prerelease = JsonDocument.Parse("""{"draft":false,"prerelease":true,"tag_name":"v9.1.2-rc.1"}""");
        if (ReadRelease(prerelease.RootElement, current, installed: false) is not null)
            throw new Exception("Updater accepted a prerelease.");
        using var unverified = JsonDocument.Parse(release.RootElement.GetRawText().Replace("sha256:", "invalid:", StringComparison.Ordinal));
        try
        {
            ReadRelease(unverified.RootElement, current, installed: false);
            throw new Exception("Updater accepted an unverified asset.");
        }
        catch (InvalidDataException) { }
    }
}
