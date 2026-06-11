using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DirectStorageUpdater
{
    internal sealed class NugetVersionIndex
    {
        [JsonPropertyName("versions")]
        public List<string> Versions { get; set; } = new();
    }

    internal sealed class DsVersion : IComparable<DsVersion>
    {
        public string  Raw       { get; }
        public Version Parsed    { get; }   // major.minor.patch (preview suffix stripped)
        public bool    IsPreview { get; }

        public DsVersion(string raw)
        {
            Raw       = raw;
            IsPreview = raw.Contains('-');

            // Strip tag
            string clean = raw.Contains('-') ? raw[..raw.IndexOf('-')] : raw;

            // 4 part version just in case
            string[] parts = clean.Split('.');
            while (parts.Length < 4) parts = [.. parts, "0"];
            Parsed = new Version(string.Join('.', parts.Take(4)));
        }

        public int CompareTo(DsVersion? other)
        {
            if (other is null) return 1;
            int c = Parsed.CompareTo(other.Parsed);
            if (c != 0) return c;
            return IsPreview.CompareTo(other.IsPreview);
        }

        public bool IsNewerThan(DsVersion other) => CompareTo(other) > 0;
        public bool OlderOrEq(DsVersion other)   => CompareTo(other) <= 0;

        public override string ToString() => Raw;
    }

    // Changelog stuff

    internal static class ChangelogParser
    {
        /// <summary>
        /// Extracts README.md from the nupkg at <paramref name="nupkgPath"/> and
        /// parses it into a dictionary of version → lines, ordered newest → oldest.
        /// </summary>
        public static Dictionary<DsVersion, string[]> ParseFromPackage(string nupkgPath)
        {
            using ZipArchive zip = ZipFile.OpenRead(nupkgPath);

            ZipArchiveEntry? entry =
                zip.GetEntry("README.md") ??
                zip.Entries.FirstOrDefault(e =>
                    e.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase));

            if (entry is null) return new();

            using StreamReader reader = new(entry.Open());
            string content = reader.ReadToEnd();
            return Parse(content);
        }

        private static Dictionary<DsVersion, string[]> Parse(string markdown)
        {
            var result = new Dictionary<DsVersion, string[]>();
            var lines  = markdown.Split('\n');

            DsVersion?    currentVersion = null;
            List<string>  currentLines   = new();

            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');

                if (line.StartsWith("## "))
                {
                    // Save previous section
                    if (currentVersion is not null && currentLines.Count > 0)
                        result[currentVersion] = TrimLines(currentLines);

                    string tag = line[3..].Trim();
                    currentVersion = TryParseVersion(tag);
                    currentLines   = new();
                    continue;
                }

                if (currentVersion is not null)
                    currentLines.Add(line);
            }

            // Save last section
            if (currentVersion is not null && currentLines.Count > 0)
                result[currentVersion] = TrimLines(currentLines);

            return result;
        }

        private static DsVersion? TryParseVersion(string tag)
        {
            try   { return new DsVersion(tag); }
            catch { return null; }
        }

        private static string[] TrimLines(List<string> lines)
        {
            int start = 0;
            while (start < lines.Count && string.IsNullOrWhiteSpace(lines[start])) start++;
            int end = lines.Count - 1;
            while (end >= start && string.IsNullOrWhiteSpace(lines[end])) end--;
            return lines.Skip(start).Take(end - start + 1).ToArray();
        }

        public static IEnumerable<(string Version, string[] Lines)> GetRange(
            Dictionary<DsVersion, string[]> changelog,
            DsVersion fromVersion,
            DsVersion toVersion)
        {
            return changelog
                .Where(kv => kv.Key.IsNewerThan(fromVersion) && !kv.Key.IsNewerThan(toVersion))
                .OrderByDescending(kv => kv.Key)
                .Select(kv => (kv.Key.Raw, kv.Value));
        }
    }

    // NuGet HTTP client

    internal sealed class NugetClient : IDisposable
    {
        private const string PackageId    = "microsoft.direct3d.directstorage";
        private const string IndexUrl     = $"https://api.nuget.org/v3-flatcontainer/{PackageId}/index.json";
        private const string DownloadBase = $"https://api.nuget.org/v3-flatcontainer/{PackageId}";

        private readonly HttpClient _http;

        public NugetClient()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "DirectStorageUpdater/1.0");
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<List<DsVersion>> GetVersionsAsync()
        {
            string json = await _http.GetStringAsync(IndexUrl);
            NugetVersionIndex? idx = JsonSerializer.Deserialize<NugetVersionIndex>(json)
                ?? throw new InvalidDataException("Invalid version index response.");

            return idx.Versions
                .Select(v => new DsVersion(v))
                .OrderByDescending(v => v)
                .ToList();
        }

        public async Task DownloadPackageAsync(
            DsVersion version,
            string destPath,
            Action<long, long> onProgress)
        {
            string url = $"{DownloadBase}/{version.Raw.ToLowerInvariant()}/microsoft.direct3d.directstorage.{version.Raw.ToLowerInvariant()}.nupkg";

            using HttpResponseMessage resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            long   total      = resp.Content.Headers.ContentLength ?? -1;
            long   downloaded = 0;
            byte[] buffer     = new byte[81920];

            await using Stream src  = await resp.Content.ReadAsStreamAsync();
            await using Stream dest = File.Create(destPath);

            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                onProgress(downloaded, total);
            }
        }

        public async Task<string?> DownloadLatestForChangelogAsync(DsVersion latestVersion)
        {
            try
            {
                string tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"ds_changelog_{Guid.NewGuid():N}.nupkg");

                await DownloadPackageAsync(latestVersion, tempPath, (_, _) => { });
                return tempPath;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose() => _http.Dispose();
    }

    // DLL inspector

    internal static class DllInspector
    {
        private static readonly string[] DllNames = ["dstorage.dll", "dstoragecore.dll"];

        public static (DsVersion? version, string? fileVersion) GetLocalVersion(string gameFolder)
        {
            foreach (string name in DllNames)
            {
                string path = Path.Combine(gameFolder, name);
                if (!File.Exists(path)) continue;

                FileVersionInfo fvi          = FileVersionInfo.GetVersionInfo(path);
                string?         productVersion = fvi.ProductVersion;
                string          fileVersion    = $"{fvi.FileMajorPart}.{fvi.FileMinorPart}.{fvi.FileBuildPart}.{fvi.FilePrivatePart}";

                if (string.IsNullOrWhiteSpace(productVersion)) continue;
                return (new DsVersion(productVersion), fileVersion);
            }

            return (null, null);
        }

        public static string[] GetDllNames() => DllNames;
    }

    // Extractor

    internal static class PackageExtractor
    {
        private const string InternalPath = "native/bin/x64/";

        /// <summary>
        /// Extracts dstorage.dll and dstoragecore.dll from the nupkg zip at
        /// <paramref name="nupkgPath"/> into <paramref name="destFolder"/>.
        /// Returns the list of extracted file paths.
        /// </summary>
        public static List<string> ExtractDlls(string nupkgPath, string destFolder)
        {
            using ZipArchive zip = ZipFile.OpenRead(nupkgPath);
            List<string> extracted = new();

            foreach (string dllName in DllInspector.GetDllNames())
            {
                string entryName = InternalPath + dllName;
                ZipArchiveEntry? entry = zip.GetEntry(entryName)
                    ?? throw new FileNotFoundException(
                        $"Expected entry '{entryName}' not found in package. " +
                        "Package layout may have changed.");

                string dest = Path.Combine(destFolder, dllName);
                entry.ExtractToFile(dest, overwrite: true);
                extracted.Add(dest);
            }

            return extracted;
        }
    }
}