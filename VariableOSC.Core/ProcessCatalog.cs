using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VariableOSC
{
    /// <summary>
    /// One running process, as shown to the user when picking a scan
    /// target.
    /// </summary>
    public class ProcessEntry
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string WindowTitle { get; set; } = "";
    }

    /// <summary>
    /// Snapshotting and filtering running processes for the process-picker
    /// UI. Shared by the console and GUI front ends so both list/filter
    /// processes identically.
    /// </summary>
    public static class ProcessCatalog
    {
        public static List<ProcessEntry> TakeSnapshot()
        {
            List<ProcessEntry> list = new();

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    int id = process.Id;
                    string name = process.ProcessName;
                    string title;

                    try
                    {
                        title = process.MainWindowTitle;
                    }
                    catch
                    {
                        title = "";
                    }

                    list.Add(new ProcessEntry
                    {
                        Id = id,
                        Name = name,
                        WindowTitle = title ?? ""
                    });
                }
                catch
                {
                    // Some processes (system/elevated) can't be inspected. Skip them.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return list
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id)
                .ToList();
        }

        public static List<ProcessEntry> Filter(
            List<ProcessEntry> source,
            bool windowedOnly,
            string? filter)
        {
            IEnumerable<ProcessEntry> query = source;

            if (windowedOnly)
            {
                query = query.Where(p => !string.IsNullOrWhiteSpace(p.WindowTitle));
            }

            if (!string.IsNullOrWhiteSpace(filter))
            {
                query = query.Where(p =>
                    p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    p.WindowTitle.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }
    }
}
