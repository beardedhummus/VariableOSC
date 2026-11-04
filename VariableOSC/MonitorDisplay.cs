using System;
using System.Collections.Generic;
using System.Threading;

namespace VariableOSC
{
    /// <summary>
    /// Shared redraw loop for every "monitor this" screen (a single scan
    /// candidate, or every saved variable). Replaces the old "print the
    /// value every tick" loops, which scrolled the terminal endlessly even
    /// when nothing was changing. Instead this redraws a fixed screen -
    /// a blinking "MONITORING" header and the current value of each
    /// watched item - plus a change log below that only grows when a
    /// value actually changes (e.g. "14:32:07  PlayerHealth: 123 -> 124").
    /// </summary>
    public static class MonitorDisplay
    {
        public static void Run(string title, Func<List<(string Label, string Value)>> readSnapshot)
        {
            Dictionary<string, string> lastValues = new();
            List<string> changeLog = new();
            bool blinkOn = true;
            DateTime lastBlinkToggle = DateTime.Now;

            while (true)
            {
                List<(string Label, string Value)> snapshot = readSnapshot();

                foreach ((string label, string value) in snapshot)
                {
                    if (lastValues.TryGetValue(label, out string? previous) && previous != value)
                    {
                        changeLog.Add($"{DateTime.Now:HH:mm:ss}  {label}: {previous} -> {value}");
                    }

                    lastValues[label] = value;
                }

                if ((DateTime.Now - lastBlinkToggle).TotalMilliseconds >= 500)
                {
                    blinkOn = !blinkOn;
                    lastBlinkToggle = DateTime.Now;
                }

                Redraw(title, snapshot, changeLog, blinkOn);

                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.Q)
                    {
                        return;
                    }
                }

                Thread.Sleep(200);
            }
        }

        private static void Redraw(
            string title,
            List<(string Label, string Value)> snapshot,
            List<string> changeLog,
            bool blinkOn)
        {
            Console.Clear();

            Console.WriteLine("=================================");
            Console.Write("  ");

            if (blinkOn)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("●");
                Console.ResetColor();
                Console.Write(" MONITORING");
            }
            else
            {
                Console.Write("  MONITORING");
            }

            Console.WriteLine();
            Console.WriteLine("=================================");
            Console.WriteLine();
            Console.WriteLine(title);
            Console.WriteLine();

            if (snapshot.Count == 0)
            {
                Console.WriteLine("  (nothing to show)");
            }

            foreach ((string label, string value) in snapshot)
            {
                Console.WriteLine($"  {label}: {value}");
            }

            Console.WriteLine();
            Console.WriteLine("---------------------------------");
            Console.WriteLine("Change log (press Q to stop):");
            Console.WriteLine("---------------------------------");

            if (changeLog.Count == 0)
            {
                Console.WriteLine("(no changes yet)");
            }
            else
            {
                foreach (string entry in changeLog)
                {
                    Console.WriteLine(entry);
                }
            }
        }
    }
}
