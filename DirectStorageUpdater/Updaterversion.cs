using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DirectStorageUpdater
{
    // ── GitHub release model ───────────────────────────────────────────────────

    internal sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";
    }

    // ── Self-version check ─────────────────────────────────────────────────────

    internal static class UpdaterVersionCheck
    {
        private const string GitHubApiUrl  =
            "https://api.github.com/repos/ExiledEye/DirectStorageUpdater/releases/latest";

        public const string NexusUrl   = "https://www.nexusmods.com/site/mods/1982";
        public const string GitHubUrl  = "https://github.com/ExiledEye/DirectStorageUpdater/releases/latest";

        /// <summary>Current assembly version (set in .csproj &lt;Version&gt;).</summary>
        public static Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        /// <summary>
        /// Fetches the latest GitHub release tag and compares with the current version.
        /// Returns the latest version tag string if an update is available, null otherwise.
        /// Silently swallows errors — network issues should not block the main flow.
        /// </summary>
        public static async Task<string?> CheckForUpdaterUpdateAsync()
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "DirectStorageUpdater");
                http.Timeout = TimeSpan.FromSeconds(10);

                string json = await http.GetStringAsync(GitHubApiUrl);
                GitHubRelease? release = JsonSerializer.Deserialize<GitHubRelease>(json);
                if (release is null) return null;

                string raw = release.TagName.TrimStart('v');
                if (!Version.TryParse(raw, out Version? latest)) return null;

                return latest > CurrentVersion ? release.TagName : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Opens the Nexus Mods mod page in the default browser.</summary>
        public static void OpenDownloadPage()
        {
            Process.Start(new ProcessStartInfo(NexusUrl) { UseShellExecute = true });
        }
    }
}