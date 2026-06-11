using System;
using System.IO;
using System.Linq;

namespace DirectStorageUpdater
{
    internal sealed class BackupManager
    {
        private const string BackupRoot   = "_backup";
        private const string BackupSubdir = "DirectStorage";
        private const string BackupPrefix = "v";

        private readonly string _gameFolder;
        private readonly string _backupDir;

        public BackupManager(string gameFolder)
        {
            _gameFolder = gameFolder;
            _backupDir  = Path.Combine(gameFolder, BackupRoot, BackupSubdir);
        }

        public DirectoryInfo[] FindBackups()
        {
            if (!Directory.Exists(_backupDir)) return [];
            return new DirectoryInfo(_backupDir)
                .GetDirectories($"{BackupPrefix}*")
                .OrderByDescending(d => d.Name)
                .ToArray();
        }

        public bool HasBackups() => FindBackups().Length > 0;

        // Create backup

        public string CreateBackup(string version)
        {
            Directory.CreateDirectory(_backupDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeVer   = version.Replace('.', '-');
            string backupDir = Path.Combine(_backupDir, $"v{safeVer}_{timestamp}");
            Directory.CreateDirectory(backupDir);

            foreach (string dll in DllInspector.GetDllNames())
            {
                string src = Path.Combine(_gameFolder, dll);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(backupDir, dll), overwrite: true);
            }

            return backupDir;
        }

        // Restore backup

        public void Restore(DirectoryInfo backupDir)
        {
            foreach (string dll in DllInspector.GetDllNames())
            {
                string src = Path.Combine(backupDir.FullName, dll);
                if (!File.Exists(src))
                    throw new FileNotFoundException(
                        $"Backup is incomplete — '{dll}' not found in {backupDir.Name}.");

                File.Copy(src, Path.Combine(_gameFolder, dll), overwrite: true);
            }
        }

        public static string FriendlyName(DirectoryInfo d)
        {
            // format: v1-2-1_20260607_143000
            try
            {
                string name    = d.Name;
                int    tsStart = name.IndexOf('_');
                string verPart = name[..tsStart].TrimStart('v');  // "1-2-1"
                string tsPart  = name[(tsStart + 1)..];           // "20260607_143000"

                string   version = verPart.Replace('-', '.');
                string[] ts      = tsPart.Split('_');
                string   date    = ts[0];
                string   time    = ts[1];
                string   dateStr = $"{date[..4]}-{date[4..6]}-{date[6..8]} {time[..2]}:{time[2..4]}:{time[4..6]}";

                return $"v{version}  (backed up {dateStr})";
            }
            catch
            {
                return d.Name;
            }
        }
    }
}