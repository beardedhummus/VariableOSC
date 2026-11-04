using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VariableOSC.Gui
{
    /// <summary>
    /// Tracks the most recently loaded session files (newest first, capped
    /// at 5) so the welcome screen can offer one-click access to them.
    /// Stored under %AppData% rather than next to the app so it survives
    /// the app being rebuilt or moved.
    /// </summary>
    public static class RecentSessionsStore
    {
        private const int MaxEntries = 5;

        private static string StorePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VariableOSC",
                "recent-sessions.json");

        public static List<string> Load()
        {
            try
            {
                if (!File.Exists(StorePath))
                {
                    return new List<string>();
                }

                string json = File.ReadAllText(StorePath);

                List<string>? paths = JsonSerializer.Deserialize<List<string>>(json);

                return (paths ?? new List<string>())
                    .Where(File.Exists)
                    .Take(MaxEntries)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public static void AddRecent(string path)
        {
            try
            {
                List<string> paths = Load();

                paths.RemoveAll(p =>
                    string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

                paths.Insert(0, path);

                if (paths.Count > MaxEntries)
                {
                    paths = paths.Take(MaxEntries).ToList();
                }

                string? directory = Path.GetDirectoryName(StorePath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(StorePath, JsonSerializer.Serialize(paths));
            }
            catch
            {
                // Recent-file tracking is a convenience, not critical - if
                // it fails (e.g. no write access) the app should carry on.
            }
        }
    }
}
