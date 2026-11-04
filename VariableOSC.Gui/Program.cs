using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using VariableOSC;

namespace VariableOSC.Gui
{
    internal static class Program
    {
        // Kept open for the app's lifetime and reused if the same process
        // is picked again, mirroring the console app's behavior.
        private static readonly Dictionary<int, MemoryScanner> openScanners = new();

        private static readonly List<MemoryVariable> savedVariables = new();

        private static readonly OscBroadcastManager broadcastManager = new();

        // Runs for the app's whole life, on whichever screen is showing:
        // when a target program is closed and relaunched, re-attaches the
        // variables that can find themselves again (see TargetReconnector).
        private static System.Windows.Forms.Timer? reconnectTimer;

        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            MainForm shell = new MainForm();

            ShowWelcome(shell);

            reconnectTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            reconnectTimer.Tick += (s, e) => ReconnectTargets();
            reconnectTimer.Start();

            Application.Run(shell);
        }

        private static void ReconnectTargets()
        {
            try
            {
                List<MemoryVariable> all = savedVariables
                    .Concat(broadcastManager.Jobs.SelectMany(j => j.Items.Select(i => i.Variable)))
                    .Distinct()
                    .ToList();

                foreach (Reconnection done in TargetReconnector.Reconnect(all, openScanners))
                {
                    ReconnectStatus.Report(
                        done.OldProcessId == 0
                            ? $"{done.ProcessName} found - attached {done.VariableCount} variable(s)."
                            : $"{done.ProcessName} restarted - re-attached {done.VariableCount} variable(s).");
                }
            }
            catch (Exception)
            {
                // A reconnect attempt must never take the app down; the
                // next tick simply tries again.
            }
        }

        private static void ShowWelcome(MainForm shell)
        {
            bool hasActiveSession = savedVariables.Count > 0 || broadcastManager.Jobs.Count > 0;

            WelcomeView welcome = new WelcomeView(hasActiveSession);

            welcome.NewSessionRequested += () => ShowProcessPicker(shell);
            welcome.ContinueSessionRequested += () => ShowDashboard(shell);

            welcome.StartOverRequested += () =>
            {
                ClearSession();
                ShowProcessPicker(shell);
            };

            welcome.SessionLoadRequested += path =>
            {
                // Any "discard the current session?" confirmation already
                // happened in WelcomeView before this fired - safe to
                // clear unconditionally (a no-op if nothing was active).
                ClearSession();
                PerformSessionLoad(shell, path);
            };

            shell.ShowView(welcome);
        }

        /// <summary>
        /// Wipes the in-memory session (saved variables + broadcasts) so
        /// starting fresh or loading a different file doesn't collide with
        /// whatever was there before. Previously "Back to Welcome" never
        /// cleared anything, so a second New Session or Load Session would
        /// silently fight the leftover state for every name - the "Name
        /// Taken" conflicts were almost always really "the same variable I
        /// already have, from a session I never actually left."
        /// </summary>
        private static void ClearSession()
        {
            broadcastManager.StopAll();
            broadcastManager.Jobs.Clear();
            savedVariables.Clear();
        }

        private static void ShowProcessPicker(MainForm shell)
        {
            ProcessPickerView picker = new ProcessPickerView();

            picker.BackRequested += () => ShowWelcome(shell);
            picker.ProcessSelected += entry => OnProcessSelected(shell, entry);

            shell.ShowView(picker);
        }

        private static void OnProcessSelected(MainForm shell, ProcessEntry entry)
        {
            MemoryScanner? scanner = GetOrOpenScanner(shell, entry.Id, entry.Name);

            if (scanner == null)
            {
                return;
            }

            ShowScan(shell, scanner, entry.Name, entry.Id);
        }

        private static void ShowScan(MainForm shell, MemoryScanner scanner, string processName, int processId)
        {
            ScanView scan = new ScanView(scanner, processName, processId);

            scan.BackRequested += () => ShowProcessPicker(shell);

            scan.VariableSaved += variable =>
            {
                string? name = NameConflictResolver.ResolveVariableName(
                    shell, savedVariables, broadcastManager, variable.Name);

                if (name == null)
                {
                    return;
                }

                variable.Name = name;
                savedVariables.Add(variable);

                DialogResult result = MessageBox.Show(
                    shell,
                    $"Saved \"{variable.Name}\" ({variable.Type}).\n\nView the dashboard now?",
                    "Variable Saved",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                {
                    ShowDashboard(shell);
                }
            };

            shell.ShowView(scan);
        }

        private static void ShowDashboard(MainForm shell)
        {
            DashboardView dashboard = new DashboardView(savedVariables, broadcastManager);

            dashboard.NewScanRequested += () => ShowProcessPicker(shell);
            dashboard.BackToWelcomeRequested += () => ShowWelcome(shell);
            dashboard.ReplaceTargetRequested += variable => ShowReplaceTarget(shell, variable);

            shell.ShowView(dashboard);
        }

        private static void ShowReplaceTarget(MainForm shell, MemoryVariable variable)
        {
            // DashboardView already checked variable.Scanner != null before
            // raising this - a variable only ever reaches here with a live
            // connection to its process.
            MemoryScanner scanner = variable.Scanner!;

            ScanView scan = new ScanView(scanner, variable.SourceProcessName, scanner.ProcessId, variable);

            scan.BackRequested += () => ShowDashboard(shell);

            scan.TargetReplaced += replaced =>
            {
                MessageBox.Show(
                    shell,
                    $"\"{replaced.Name}\" now points at 0x{replaced.Address.ToInt64():X}.",
                    "Target Replaced",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                ShowDashboard(shell);
            };

            shell.ShowView(scan);
        }

        private static MemoryScanner? GetOrOpenScanner(MainForm shell, int processId, string processName)
        {
            if (openScanners.TryGetValue(processId, out MemoryScanner? existing))
            {
                return existing;
            }

            try
            {
                // MemoryScanner keeps this Process object for its entire
                // lifetime (Modules/Id lookups for pointer paths), so it
                // must NOT be disposed here - doing so used to throw
                // "No process is associated with this object" on every
                // later Modules/Id access, silently swallowed by
                // SafeListModules (making every Convert to Pointer Path
                // wrongly report "not inside any loaded module") and
                // unguarded in ProcessId (a hard crash on Replace Target).
                Process process = Process.GetProcessById(processId);

                MemoryScanner scanner = new MemoryScanner(process);
                openScanners[processId] = scanner;
                return scanner;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    shell,
                    $"Could not open {processName}: {ex.Message}\n\n" +
                    "VariABLE OSC and the target program usually need to match - both 32-bit or " +
                    "both 64-bit - and you may need to run as Administrator.",
                    "Could Not Open Process",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return null;
            }
        }

        // ------------------------------------------------------------
        // Session load - GUI equivalent of the console's LoadSession.
        // Reconnects by process name (not PID), resolves pointer paths
        // against the (possibly restarted) target, and routes every name
        // conflict through the same dialog as a live save.
        // ------------------------------------------------------------

        private static void PerformSessionLoad(MainForm shell, string path)
        {
            SessionData data;

            try
            {
                data = SessionStore.Load(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    shell,
                    $"Could not read session file:\n{ex.Message}",
                    "Load Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            List<string> notes = new();
            Dictionary<string, MemoryScanner> resolvedByProcessName = new(StringComparer.OrdinalIgnoreCase);
            int loadedVariableCount = 0;
            int loadedJobCount = 0;

            foreach (SessionVariableData variableData in data.SavedVariables)
            {
                MemoryVariable? variable = ResolveSessionVariable(shell, variableData, resolvedByProcessName, notes);

                if (variable != null)
                {
                    savedVariables.Add(variable);
                    loadedVariableCount++;
                }
            }

            foreach (SessionJobData jobData in data.Jobs)
            {
                string? jobName = NameConflictResolver.ResolveJobName(shell, broadcastManager, jobData.Name);

                if (jobName == null)
                {
                    notes.Add($"Skipped broadcast \"{jobData.Name}\" - cancelled.");
                    continue;
                }

                OscJob job = new OscJob
                {
                    Name = jobName,
                    IpAddress = jobData.IpAddress,
                    Port = jobData.Port,
                    IsGroup = jobData.IsGroup
                };

                foreach (SessionJobItemData itemData in jobData.Items)
                {
                    MemoryVariable? variable =
                        ResolveSessionVariable(shell, itemData.Variable, resolvedByProcessName, notes);

                    if (variable != null)
                    {
                        job.Items.Add(new OscBroadcastItem(variable, itemData.OscAddress));
                    }
                }

                if (job.Items.Count == 0)
                {
                    notes.Add($"Skipped broadcast \"{job.Name}\" - none of its variables could be reconnected.");
                    continue;
                }

                broadcastManager.Jobs.Add(job);
                loadedJobCount++;

                if (jobData.WasRunning)
                {
                    DialogResult resume = MessageBox.Show(
                        shell,
                        $"Resume sending \"{job.Name}\" now?",
                        "Resume Broadcast",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (resume == DialogResult.Yes)
                    {
                        try
                        {
                            job.Start();
                        }
                        catch (Exception ex)
                        {
                            notes.Add($"Could not start \"{job.Name}\": {ex.Message}");
                        }
                    }
                }
            }

            // Report what actually got reconnected, not the file's raw
            // counts - those looked identical only when nothing was
            // skipped, and silently wrong (e.g. "loaded 1 broadcast" for
            // one the user just cancelled) whenever a name conflict or a
            // missing process caused something to be skipped.
            string summary =
                $"Loaded {loadedVariableCount} variable(s) and {loadedJobCount} broadcast(s).";

            if (notes.Count > 0)
            {
                summary += "\n\n" + string.Join("\n", notes);
            }

            MessageBox.Show(shell, summary, "Session Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);

            ShowDashboard(shell);
        }

        private static MemoryVariable? ResolveSessionVariable(
            MainForm shell,
            SessionVariableData data,
            Dictionary<string, MemoryScanner> resolvedByProcessName,
            List<string> notes)
        {
            if (!resolvedByProcessName.TryGetValue(data.ProcessName, out MemoryScanner? scanner))
            {
                scanner = ResolveScannerByProcessName(shell, data.ProcessName);
                resolvedByProcessName[data.ProcessName] = scanner!;
            }

            bool canWait =
                data.PointerPath != null ||
                data.PermanentPaths.Count > 0 ||
                data.PermanenceCandidates.Count > 0;

            if (scanner == null && !canWait)
            {
                notes.Add($"Skipped \"{data.Name}\" - \"{data.ProcessName}\" is not currently running.");
                return null;
            }

            string? name = NameConflictResolver.ResolveVariableName(shell, savedVariables, broadcastManager, data.Name);

            if (name == null)
            {
                notes.Add($"Skipped \"{data.Name}\" - cancelled.");
                return null;
            }

            PointerPath? pointerPath = SessionStore.ToPointerPath(data.PointerPath);
            IntPtr address = new IntPtr(data.Address);

            if (scanner == null)
            {
                notes.Add(
                    $"\"{name}\" is waiting for {data.ProcessName} to start - it will attach on its own.");
            }
            else if (pointerPath != null)
            {
                IntPtr? resolved = scanner.ResolvePointerPath(pointerPath);

                if (resolved != null)
                {
                    address = resolved.Value;
                }
                else
                {
                    notes.Add(
                        $"\"{name}\"'s pointer path did not resolve - loaded with its last-known address. " +
                        "Use Convert to Pointer Path / re-scan if it doesn't read correctly.");
                }
            }
            else if (data.PermanentPaths.Count == 0 && data.PermanenceCandidates.Count == 0)
            {
                notes.Add(
                    $"\"{name}\" has no pointer path - its address may be stale if {data.ProcessName} was restarted.");
            }

            MemoryVariable variable = new MemoryVariable(name, address, data.Type)
            {
                Scanner = scanner,
                SourceProcessName = data.ProcessName,
                StringEncoding = data.StringEncoding,
                StringLength = data.StringLength,
                PointerPath = pointerPath
            };

            SessionStore.ApplyPermanence(data, variable);

            if (scanner != null && variable.PermanentPaths.Count > 0)
            {
                // Resolves the chains now so the address and health are
                // current the moment the dashboard appears.
                if (scanner.ReadValue(variable) == null)
                {
                    notes.Add(
                        $"\"{name}\": its saved chains don't lead anywhere yet - {data.ProcessName} may " +
                        "still be loading. It will pick up on its own once the value exists.");
                }
            }
            else if (scanner != null && variable.PermanenceState == PermanenceState.Learned)
            {
                notes.Add(
                    PermanenceWorkflow.HasRestartedSinceLearning(variable)
                        ? $"\"{name}\": {data.ProcessName} was restarted since the scan - open Make Permanent to finish the check."
                        : $"\"{name}\": scan stored - restart {data.ProcessName}, then open Make Permanent to finish.");
            }

            return variable;
        }

        private static MemoryScanner? ResolveScannerByProcessName(MainForm shell, string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            Process[] matches = Process.GetProcessesByName(processName);

            try
            {
                if (matches.Length == 0)
                {
                    return null;
                }

                Process chosen = matches[0];

                if (matches.Length > 1)
                {
                    using ChoiceDialog dialog = new ChoiceDialog(
                        "Multiple Instances",
                        $"Multiple running instances of \"{processName}\" found - which one has the variables?",
                        matches.Select(m => $"PID {m.Id}"));

                    if (dialog.ShowDialog(shell) != DialogResult.OK || dialog.SelectedIndex < 0)
                    {
                        return null;
                    }

                    chosen = matches[dialog.SelectedIndex];
                }

                return GetOrOpenScanner(shell, chosen.Id, processName);
            }
            finally
            {
                foreach (Process process in matches)
                {
                    process.Dispose();
                }
            }
        }
    }
}
