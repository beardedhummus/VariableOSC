using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VariableOSC
{
    public sealed record Reconnection(string ProcessName, int OldProcessId, int NewProcessId, int VariableCount);

    /// <summary>
    /// Re-attaches variables to a relaunched target. When a program is
    /// closed and started again it gets a new process ID, so every variable
    /// is still holding a scanner for a process that no longer exists.
    /// Variables that can find themselves again without a raw address
    /// (learned chains or a hand-made pointer path) are moved to a scanner
    /// for the new process; the rest are left alone, since a raw address
    /// would read garbage in the new process.
    /// </summary>
    public static class TargetReconnector
    {
        public static List<Reconnection> Reconnect(
            IEnumerable<MemoryVariable> variables,
            IDictionary<int, MemoryScanner> scannerCache)
        {
            List<Reconnection> done = new();

            IEnumerable<IGrouping<MemoryScanner, MemoryVariable>> groups = variables
                .Where(v => v.Scanner != null)
                .GroupBy(v => v.Scanner!)
                .ToList();

            foreach (IGrouping<MemoryScanner, MemoryVariable> group in groups)
            {
                MemoryScanner dead = group.Key;

                if (!dead.HasExited)
                {
                    continue;
                }

                List<MemoryVariable> eligible = group.Where(v => v.CanSurviveRestart).ToList();

                if (eligible.Count == 0)
                {
                    continue;
                }

                string processName = dead.ProcessName.Length > 0
                    ? dead.ProcessName
                    : eligible[0].SourceProcessName;

                MemoryScanner? replacement = FindReplacement(processName, dead, scannerCache);

                if (replacement == null)
                {
                    continue;
                }

                foreach (MemoryVariable variable in eligible)
                {
                    variable.Scanner = replacement;
                }

                done.Add(new Reconnection(processName, dead.ProcessId, replacement.ProcessId, eligible.Count));
            }

            // Variables loaded while their program wasn't running have no
            // scanner at all; attach them as soon as the program appears.
            IEnumerable<IGrouping<string, MemoryVariable>> waiting = variables
                .Where(v => v.Scanner == null && v.CanSurviveRestart && v.SourceProcessName.Length > 0)
                .GroupBy(v => v.SourceProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (IGrouping<string, MemoryVariable> group in waiting)
            {
                MemoryScanner? found = FindReplacement(group.Key, null, scannerCache);

                if (found == null)
                {
                    continue;
                }

                List<MemoryVariable> members = group.ToList();

                foreach (MemoryVariable variable in members)
                {
                    variable.Scanner = found;
                }

                done.Add(new Reconnection(group.Key, 0, found.ProcessId, members.Count));
            }

            // Forget scanners for processes that are gone so the cache
            // doesn't grow by one entry per target restart.
            foreach (int pid in scannerCache.Where(pair => pair.Value.HasExited).Select(pair => pair.Key).ToList())
            {
                scannerCache.Remove(pid);
            }

            return done;
        }

        private static MemoryScanner? FindReplacement(
            string processName,
            MemoryScanner? dead,
            IDictionary<int, MemoryScanner> scannerCache)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            Process[] matches;

            try
            {
                matches = Process.GetProcessesByName(processName);
            }
            catch (Exception)
            {
                return null;
            }

            try
            {
                // Newest first: after a restart the relaunched copy is the
                // most recently started one.
                foreach (Process candidate in matches.OrderByDescending(SafeStartTime))
                {
                    if (dead != null && candidate.Id == dead.ProcessId)
                    {
                        continue;
                    }

                    if (scannerCache.TryGetValue(candidate.Id, out MemoryScanner? cached) && !cached.HasExited)
                    {
                        return cached;
                    }

                    try
                    {
                        // The scanner keeps this Process object for its
                        // lifetime, so only dispose the ones not used.
                        MemoryScanner scanner = new MemoryScanner(candidate);
                        scannerCache[candidate.Id] = scanner;
                        return scanner;
                    }
                    catch (Exception)
                    {
                        // Still starting up or not accessible yet - try
                        // again on the next tick.
                    }
                }

                return null;
            }
            finally
            {
                foreach (Process process in matches)
                {
                    if (!scannerCache.Values.Any(s => s.ProcessId == process.Id))
                    {
                        process.Dispose();
                    }
                }
            }
        }

        private static DateTime SafeStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }
    }
}
