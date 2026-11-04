using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VariableOSC
{
    /// <summary>
    /// Lets the user browse and search the list of running processes and
    /// pick one to scan. Replaces the old hardcoded "look for MemoryOSC" logic.
    /// </summary>
    public static class ProcessPicker
    {
        public static Process? Choose()
        {
            List<ProcessEntry> snapshot = ProcessCatalog.TakeSnapshot();
            bool windowedOnly = true;
            string? filter = null;

            while (true)
            {
                List<ProcessEntry> visible = ProcessCatalog.Filter(snapshot, windowedOnly, filter);

                Console.WriteLine();
                Console.WriteLine("=================================");
                Console.WriteLine("       SELECT A PROCESS");
                Console.WriteLine("=================================");
                Console.WriteLine();

                if (filter != null)
                {
                    Console.WriteLine($"Filter: \"{filter}\"  (C to clear)");
                }

                Console.WriteLine(
                    windowedOnly
                        ? "Showing windowed applications only.  (A to show all processes)"
                        : "Showing all processes.  (A to show windowed applications only)");

                Console.WriteLine();

                if (visible.Count == 0)
                {
                    Console.WriteLine("No processes match.");
                }
                else
                {
                    for (int i = 0; i < visible.Count; i++)
                    {
                        ProcessEntry entry = visible[i];

                        string title =
                            string.IsNullOrWhiteSpace(entry.WindowTitle)
                                ? ""
                                : $" - \"{entry.WindowTitle}\"";

                        Console.WriteLine(
                            $"{i + 1,4} - {entry.Name} (PID {entry.Id}){title}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine(
                    "S - Search   C - Clear filter   A - Toggle all/windowed   R - Refresh   Q - Cancel");
                Console.WriteLine();
                Console.Write("Selection: ");

                string? input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                {
                    continue;
                }

                switch (input.ToUpperInvariant())
                {
                    case "Q":
                        return null;

                    case "R":
                        snapshot = ProcessCatalog.TakeSnapshot();
                        continue;

                    case "A":
                        windowedOnly = !windowedOnly;
                        continue;

                    case "C":
                        filter = null;
                        continue;

                    case "S":
                        Console.Write("Search for: ");
                        string? term = Console.ReadLine();
                        filter = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
                        continue;
                }

                if (int.TryParse(input, out int selection) &&
                    selection >= 1 &&
                    selection <= visible.Count)
                {
                    ProcessEntry chosen = visible[selection - 1];

                    try
                    {
                        return Process.GetProcessById(chosen.Id);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine();
                        Console.WriteLine("That process has closed. Refreshing the list...");

                        snapshot = ProcessCatalog.TakeSnapshot();
                        continue;
                    }
                }

                Console.WriteLine("Invalid selection.");
            }
        }
    }
}
