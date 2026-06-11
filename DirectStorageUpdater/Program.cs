using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DirectStorageUpdater
{
    internal static class Program
    {
        private static readonly string GameFolder =
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        static async Task<int> Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "DirectStorageUpdater";

            UI.Title(UpdaterVersionCheck.CurrentVersion.ToString(3));
            UI.Muted($"Game folder: {GameFolder}");

            // ── Updater self-update check ───────────────────────────────────────────
            UI.Section("Checking for Updater Updates");
            UI.Info("Checking GitHub for a newer version of this tool …");
            string? newUpdaterVersion = await UpdaterVersionCheck.CheckForUpdaterUpdateAsync();

            if (newUpdaterVersion is not null)
            {
                UI.Warn($"A new version of DirectStorageUpdater is available: {newUpdaterVersion}");
                bool openPage = UI.AskYesNo("Open download page?", defaultYes: true);
                if (openPage)
                {
                    UpdaterVersionCheck.OpenDownloadPage();
                    UI.Muted($"Also available on GitHub: {UpdaterVersionCheck.GitHubUrl}");
                }
            }
            else
            {
                UI.Ok("DirectStorageUpdater is up to date.");
            }

            try
            {
                var backup = new BackupManager(GameFolder);

                if (backup.HasBackups())
                {
                    bool handled = await HandleRestorePrompt(backup);
                    if (handled) return 0;
                }

                UI.Section("Local DLL Version");

                var (localVersion, localFileVersion) = DllInspector.GetLocalVersion(GameFolder);
                if (localVersion is null)
                {
                    UI.Error("Could not find dstorage.dll or dstoragecore.dll in this folder.");
                    UI.Info("Make sure you placed this tool in the game's root folder.");
                    UI.PressAnyKey();
                    return 1;
                }

                UI.VersionLine("Installed version:", localVersion.Raw, fileVersion: localFileVersion);

                UI.Section("Checking NuGet for Updates");
                UI.Info("Contacting api.nuget.org …");

                using var nuget = new NugetClient();
                List<DsVersion> allVersions;

                try
                {
                    allVersions = await nuget.GetVersionsAsync();
                }
                catch (Exception ex)
                {
                    UI.Error($"Failed to reach NuGet: {ex.Message}");
                    UI.PressAnyKey();
                    return 1;
                }

                // Version filter
                List<DsVersion> newer = allVersions
                    .Where(v => v.IsNewerThan(localVersion))
                    .OrderByDescending(v => v)
                    .ToList();

                DsVersion? latestStable = allVersions.FirstOrDefault(v => !v.IsPreview);

                // Fetch changelog
                DsVersion latestAny = allVersions.First();
                Dictionary<DsVersion, string[]> changelog = new();
                string? changelogTempPath = await nuget.DownloadLatestForChangelogAsync(latestAny);
                if (changelogTempPath is not null)
                {
                    try { changelog = ChangelogParser.ParseFromPackage(changelogTempPath); }
                    catch { /* best effort */ }
                    finally
                    {
                        try { File.Delete(changelogTempPath); } catch { }
                    }
                }

                // Show current status
                if (latestStable is not null)
                    UI.VersionLine("Latest stable:", latestStable.Raw, isLatest: true);

                if (newer.Count == 0)
                {
                    UI.Ok("You are already on the latest version. No update needed.");
                    UI.PressAnyKey();
                    return 0;
                }

                UI.Warn($"{newer.Count} newer version(s) available.");

                UI.Section("Available Updates");

                // Default = first non-preview in the newer list
                int defaultIdx = newer.FindIndex(v => !v.IsPreview);
                bool onlyPreviews = defaultIdx < 0;

                for (int i = 0; i < newer.Count; i++)
                {
                    DsVersion v = newer[i];
                    bool isRec = i == defaultIdx;
                    UI.ListItem(i + 1, v.Raw, recommended: isRec, isPreview: v.IsPreview);
                }

                UI.Divider();
                int? chosen = UI.AskChoiceOrSkip(newer.Count, onlyPreviews ? (int?)null : defaultIdx);
                if (chosen is null)
                {
                    UI.Info("No update selected.");
                    UI.PressAnyKey();
                    return 0;
                }
                DsVersion target = newer[chosen.Value];

                UI.Info($"Selected: {target.Raw}");

                // Backup stuff
                UI.Section("Backup");
                UI.Info("Backing up current DLLs …");

                string backupPath;
                try
                {
                    backupPath = backup.CreateBackup(localVersion.Raw);
                    UI.Ok($"Backup saved to: {Path.GetFileName(backupPath)}");
                }
                catch (Exception ex)
                {
                    UI.Error($"Backup failed: {ex.Message}");
                    if (!UI.AskYesNo("Continue without backup?", defaultYes: false))
                    {
                        UI.PressAnyKey();
                        return 1;
                    }
                    backupPath = string.Empty;
                }

                // Download the updated package
                UI.Section("Download");

                string tempDir = Path.Combine(Path.GetTempPath(), $"ds_updater_{Guid.NewGuid():N}");
                string nupkgPath = Path.Combine(tempDir, "package.nupkg");
                Directory.CreateDirectory(tempDir);

                UI.Info($"Downloading v{target.Raw} …");
                Console.WriteLine();

                try
                {
                    await nuget.DownloadPackageAsync(target, nupkgPath, (dl, total) =>
                    {
                        UI.Progress(dl, total);
                    });
                    UI.Progress(new FileInfo(nupkgPath).Length, new FileInfo(nupkgPath).Length, done: true);
                }
                catch (Exception ex)
                {
                    UI.Error($"Download failed: {ex.Message}");
                    Cleanup(tempDir);
                    UI.PressAnyKey();
                    return 1;
                }

                UI.Section("Extract & Install");
                UI.Info("Extracting DLLs from package …");

                try
                {
                    List<string> extracted = PackageExtractor.ExtractDlls(nupkgPath, GameFolder);
                    foreach (string f in extracted)
                        UI.Ok($"Installed: {Path.GetFileName(f)}");
                }
                catch (Exception ex)
                {
                    UI.Error($"Extraction failed: {ex.Message}");

                    // Try to restore backup automatically
                    if (!string.IsNullOrEmpty(backupPath))
                    {
                        UI.Warn("Attempting to restore backup …");
                        try
                        {
                            backup.Restore(new System.IO.DirectoryInfo(backupPath));
                            UI.Ok("Backup restored.");
                        }
                        catch (Exception rex)
                        {
                            UI.Error($"Restore also failed: {rex.Message}");
                        }
                    }

                    Cleanup(tempDir);
                    UI.PressAnyKey();
                    return 1;
                }
                finally
                {
                    Cleanup(tempDir);
                }

                UI.Section("Update Complete");
                UI.Ok($"DirectStorage updated: v{localVersion.Raw}  →  v{target.Raw}");

                // Ask if changelog
                Console.WriteLine();
                bool showLog = UI.AskYesNo("Show changelog for updated versions?", defaultYes: true);

                if (showLog)
                {
                    UI.Section("Changelog");
                    var entries = ChangelogParser.GetRange(changelog, localVersion, target).ToList();
                    if (entries.Count == 0)
                    {
                        UI.Muted("No changelog entries found for this range.");
                    }
                    else
                    {
                        foreach (var (version, lines) in entries)
                            UI.PrintChangelog(version, lines);
                    }
                }

                UI.PressAnyKey();
                return 0;
            }
            catch (Exception ex)
            {
                UI.Error($"Unexpected error: {ex.Message}");
#if DEBUG
                UI.Muted(ex.StackTrace ?? "");
#endif
                UI.PressAnyKey();
                return 1;
            }
        }


        private static Task<bool> HandleRestorePrompt(BackupManager backup)
        {
            UI.Section("Backup Files Detected");

            DirectoryInfo[] backups = backup.FindBackups();
            UI.Warn($"Found {backups.Length} backup(s) in this folder.");
            Console.WriteLine();

            for (int i = 0; i < backups.Length; i++)
                UI.ListItem(i + 1, BackupManager.FriendlyName(backups[i]));

            UI.Divider();
            bool wantRestore = UI.AskYesNo("Restore a backup? (No = continue to update check)", defaultYes: false);

            if (!wantRestore) return Task.FromResult(false);

            int pick = backups.Length == 1
                ? 0
                : UI.AskChoice(backups.Length, 0);

            DirectoryInfo chosen = backups[pick];
            UI.Info($"Restoring from: {BackupManager.FriendlyName(chosen)} …");

            try
            {
                backup.Restore(chosen);
                UI.Ok("Restore complete. DLLs have been reverted.");

                bool del = UI.AskYesNo("Delete this backup after restore?", defaultYes: true);
                if (del)
                {
                    chosen.Delete(recursive: true);
                    UI.Muted("Backup deleted.");
                }
            }
            catch (Exception ex)
            {
                UI.Error($"Restore failed: {ex.Message}");
            }

            UI.PressAnyKey();
            return Task.FromResult(true);
        }


        private static void Cleanup(string tempDir)
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
