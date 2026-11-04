using System;
using System.Collections.Generic;

namespace VariableOSC
{
    /// <summary>
    /// Monitors every saved variable at once via the shared MonitorDisplay
    /// redraw loop (blinking header + a change log that only grows when a
    /// value actually changes), rather than printing every value on every
    /// tick.
    /// </summary>
    public class LiveVariableMonitor
    {
        private readonly List<MemoryVariable> variables;

        public LiveVariableMonitor(List<MemoryVariable> variables)
        {
            this.variables = variables;
        }

        public void Start()
        {
            if (variables.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("No saved variables to monitor.");
                return;
            }

            MonitorDisplay.Run("Watching every saved variable:", ReadSnapshot);
        }

        private List<(string Label, string Value)> ReadSnapshot()
        {
            List<(string Label, string Value)> snapshot = new();

            foreach (MemoryVariable variable in variables)
            {
                object? value = variable.Scanner?.ReadValue(variable);

                variable.CurrentValue = value;

                string label =
                    $"{variable.Name} [{variable.SourceProcessName}] ({variable.Type})";

                string displayValue = value == null
                    ? "<read failed>"
                    : variable.Type == DataType.String
                        ? DisplayText.Sanitize(value.ToString())
                        : value.ToString() ?? "<read failed>";

                snapshot.Add((label, displayValue));
            }

            return snapshot;
        }
    }
}
