using System;
using System.Globalization;
using System.IO;

namespace Haugbui.Core.Persistence
{
    public enum BackupDecision
    {
        Off,
        NoData,
        NotLocal,
        Saving,
        Copy
    }

    /// <summary>One timestamped copy of a world folder, placed next to it, so it can be copied back over the original.</summary>
    public static class WorldBackup
    {
        public const string Prefix = "haugbui_backup_";

        public static BackupDecision Choose(bool enabled, bool hasData, bool localFolder, bool saving)
        {
            if (!enabled) return BackupDecision.Off;
            if (!hasData) return BackupDecision.NoData;
            if (!localFolder) return BackupDecision.NotLocal;
            if (saving) return BackupDecision.Saving;
            return BackupDecision.Copy;
        }

        public static string FolderName(string worldName, DateTime localTime)
        {
            var safe = new char[worldName == null ? 0 : worldName.Length];
            for (int i = 0; i < safe.Length; i++)
            {
                char c = worldName[i];
                safe[i] = c < 32 || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c;
            }
            string name = new string(safe).Trim();
            if (name.Length == 0) name = "world";
            return Prefix + name + "_" + localTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }

        /// <summary>Copies the tree. Returns false when <paramref name="shouldStop"/> asked it to stop; the caller deletes the partial folder.</summary>
        public static bool CopyTree(string sourceDir, string destDir, Func<bool> shouldStop)
        {
            if (shouldStop != null && shouldStop()) return false;
            Directory.CreateDirectory(destDir);
            foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (shouldStop != null && shouldStop()) return false;
                Directory.CreateDirectory(Path.Combine(destDir, Relative(sourceDir, dir)));
            }
            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (shouldStop != null && shouldStop()) return false;
                string target = Path.Combine(destDir, Relative(sourceDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, overwrite: true);
            }
            return shouldStop == null || !shouldStop();
        }

        private static string Relative(string root, string path)
        {
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path.Substring(prefix.Length) : Path.GetFileName(path);
        }
    }
}
