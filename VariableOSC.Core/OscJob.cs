using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VariableOSC
{
    /// <summary>
    /// One saved variable, paired with the OSC address it's sent to
    /// under a given OscJob.
    /// </summary>
    public class OscBroadcastItem
    {
        public MemoryVariable Variable { get; }

        public string OscAddress { get; set; }

        public OscBroadcastItem(MemoryVariable variable, string oscAddress)
        {
            Variable = variable;
            OscAddress = oscAddress;
        }
    }

    /// <summary>
    /// A single OSC destination (IP + port) and the variable(s) sent to it.
    /// Used both for a single variable sent on its own and for named
    /// groups containing several variables - the only difference is
    /// IsGroup, which is purely cosmetic for display purposes.
    ///
    /// Each variable carries its own MemoryScanner reference, so a single
    /// job can freely mix variables read from different target processes.
    ///
    /// Sending runs on a background Task so the console menu stays
    /// responsive. The background loop never writes to the console
    /// directly (to avoid garbling whatever the user is typing) - it only
    /// records the most recent error, which the menu screens display.
    /// </summary>
    public class OscJob
    {
        public string Name { get; set; } = "";

        public string IpAddress { get; set; } = "";

        public int Port { get; set; }

        public bool IsGroup { get; set; }

        public List<OscBroadcastItem> Items { get; } = new();

        public string? LastError { get; private set; }

        private CancellationTokenSource? cts;
        private Task? task;

        public bool IsRunning =>
            task != null &&
            !task.IsCompleted &&
            cts != null &&
            !cts.IsCancellationRequested;

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            if (Items.Count == 0)
            {
                throw new InvalidOperationException(
                    "This broadcast has no variables to send yet.");
            }

            // Validate the destination up front so a bad IP/port fails
            // immediately instead of silently dying on the background thread.
            using (new OscSender(IpAddress, Port))
            {
            }

            cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            task = Task.Run(() => RunLoop(token), token);
        }

        public void Stop()
        {
            cts?.Cancel();
        }

        private void RunLoop(CancellationToken token)
        {
            try
            {
                using OscSender sender = new OscSender(IpAddress, Port);

                while (!token.IsCancellationRequested)
                {
                    foreach (OscBroadcastItem item in Items)
                    {
                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        MemoryVariable variable = item.Variable;

                        if (variable.Scanner == null)
                        {
                            LastError = $"{variable.Name}: no scanner available";
                            continue;
                        }

                        object? value =
                            variable.Scanner.ReadValue(variable);

                        if (value == null)
                        {
                            LastError = $"{variable.Name}: could not read from memory";
                            continue;
                        }

                        variable.CurrentValue = value;

                        try
                        {
                            sender.SendValue(
                                item.OscAddress,
                                value,
                                variable.Type);

                            LastError = null;
                        }
                        catch (Exception ex)
                        {
                            LastError = $"{variable.Name}: {ex.Message}";
                        }
                    }

                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }
}
