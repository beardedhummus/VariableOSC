using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// The Make Permanent wizard. Three steps:
    ///  1. Learn - scan the running program for every pointer chain that
    ///     leads to the chosen variables.
    ///  2. Restart check - after the program has been restarted (detected
    ///     automatically; the reconnect timer re-attaches the variables),
    ///     type what each variable shows and keep the chains that lead to
    ///     that value.
    ///  3. Confirm - change the value in the program and type the new
    ///     value; only chains that follow the change survive. (Skipped for
    ///     long, distinctive text, which can't match by coincidence.)
    /// The same check page also serves as "re-check": recovering a lost
    /// variable from the saved scan without scanning again.
    /// Everything it learns is stored on the variables themselves, so the
    /// wizard can be closed between steps and picked up later.
    /// </summary>
    public class MakePermanentDialog : Form
    {
        private readonly List<MemoryVariable> candidates;
        private readonly MemoryVariable invoker;
        private readonly string processName;
        private readonly Action<IWin32Window>? saveSession;
        private readonly MemoryScanner? scannerAtOpen;

        private readonly Label stepLabel;

        private readonly Panel learnPanel;
        private readonly Label learnIntro;
        private readonly CheckedListBox variablesList;
        private readonly ComboBox depthCombo;
        private readonly Label depthHint;
        private readonly ProgressBar progressBar;
        private readonly Label learnStatus;
        private readonly Button scanButton;
        private readonly Button cancelScanButton;

        private readonly Panel verifyPanel;
        private readonly Label verifyIntro;
        private readonly Label restartStatus;
        private readonly Panel rowsPanel;
        private readonly Button verifyButton;
        private readonly Button backToScanButton;
        private readonly Label resultLabel;

        private readonly Button continueButton;
        private readonly Button useSavedScanButton;

        private readonly Button closeButton;
        private readonly Button saveButton;

        private readonly System.Windows.Forms.Timer tick;

        private readonly List<bool> readable = new();
        private readonly List<VerifyRow> rows = new();

        private CancellationTokenSource? scanCancel;
        private bool scanning;
        private bool onVerifyPage;
        private CheckMode mode = CheckMode.Restart;

        private enum CheckMode
        {
            /// <summary>Step 2: first look at the whole scan after a restart.</summary>
            Restart,

            /// <summary>Recover a lost variable from the saved scan, no restart needed.</summary>
            Recheck,

            /// <summary>Step 3: change the value, then keep chains that follow it.</summary>
            Confirm
        }

        /// <summary>True if any variable's permanence data changed, so the caller knows to refresh.</summary>
        public bool Changed { get; private set; }

        private sealed class VerifyRow
        {
            public MemoryVariable Variable = null!;
            public TextBox Input = null!;
            public Label Result = null!;

            /// <summary>Succeeded this round; its input stays locked.</summary>
            public bool Done;
        }

        public MakePermanentDialog(
            List<MemoryVariable> processVariables,
            MemoryVariable invoker,
            Action<IWin32Window>? saveSession)
        {
            this.candidates = processVariables;
            this.invoker = invoker;
            this.saveSession = saveSession;

            processName = string.IsNullOrWhiteSpace(invoker.SourceProcessName)
                ? "the program"
                : invoker.SourceProcessName;

            scannerAtOpen = invoker.Scanner;

            AutoScaleMode = AutoScaleMode.None;
            Text = "Make Permanent";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(680, 566);

            Label title = new Label
            {
                Text = "Make Permanent",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 14)
            };

            stepLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = SystemColors.GrayText,
                Location = new Point(22, 48),
                Size = new Size(630, 20)
            };

            // ---------------- Step 1: learn ----------------

            learnPanel = new Panel { Location = new Point(20, 78), Size = new Size(640, 416) };

            learnIntro = new Label
            {
                Text =
                    $"VariABLE OSC will search {processName}'s memory for every chain of pointers that leads " +
                    "to the variables you tick below. Restarting the program afterwards weeds out the false " +
                    "ones, leaving chains that find each variable again on their own.\n" +
                    $"Make sure {processName} is showing the correct values right now.",
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(0, 0),
                Size = new Size(640, 76)
            };

            Label listLabel = new Label
            {
                Text = "Variables to make permanent:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(0, 82),
                Size = new Size(640, 20)
            };

            variablesList = new CheckedListBox
            {
                Location = new Point(0, 104),
                Size = new Size(640, 130),
                CheckOnClick = true,
                Font = new Font("Segoe UI", 9.5f),
                IntegralHeight = false
            };

            Label depthLabel = new Label
            {
                Text = "Search depth:",
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(0, 246),
                Size = new Size(110, 22)
            };

            depthCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(116, 243),
                Size = new Size(330, 26),
                Font = new Font("Segoe UI", 9.5f)
            };

            depthCombo.Items.Add("Quick - fastest, shallow chains");
            depthCombo.Items.Add("Standard - recommended");
            depthCombo.Items.Add("Deep - slower, finds longer chains");
            depthCombo.SelectedIndex = 1;

            depthHint = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = SystemColors.GrayText,
                Location = new Point(0, 274),
                Size = new Size(640, 20)
            };

            progressBar = new ProgressBar
            {
                Location = new Point(0, 306),
                Size = new Size(640, 18),
                Minimum = 0,
                Maximum = 1000,
                Style = ProgressBarStyle.Continuous
            };

            learnStatus = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(0, 330),
                Size = new Size(640, 44)
            };

            scanButton = MakeButton("Start Scan", 0, 380, 150);
            cancelScanButton = MakeButton("Cancel Scan", 160, 380, 130);
            useSavedScanButton = MakeButton("Re-check Saved Scan >", 300, 380, 230);
            useSavedScanButton.Click += (s, e) => ShowCheckPage(CheckMode.Recheck);

            scanButton.Click += (s, e) => StartScan();
            cancelScanButton.Click += (s, e) => scanCancel?.Cancel();
            depthCombo.SelectedIndexChanged += (s, e) => UpdateDepthHint();

            variablesList.ItemCheck += (s, e) =>
            {
                // A variable that can't be read right now has nothing to
                // trace back from; keep it unticked.
                if (e.Index < readable.Count && !readable[e.Index] && e.NewValue == CheckState.Checked)
                {
                    e.NewValue = CheckState.Unchecked;
                }
            };

            learnPanel.Controls.Add(learnIntro);
            learnPanel.Controls.Add(listLabel);
            learnPanel.Controls.Add(variablesList);
            learnPanel.Controls.Add(depthLabel);
            learnPanel.Controls.Add(depthCombo);
            learnPanel.Controls.Add(depthHint);
            learnPanel.Controls.Add(progressBar);
            learnPanel.Controls.Add(learnStatus);
            learnPanel.Controls.Add(scanButton);
            learnPanel.Controls.Add(cancelScanButton);
            learnPanel.Controls.Add(useSavedScanButton);

            // ---------------- Step 2: verify ----------------

            verifyPanel = new Panel { Location = new Point(20, 78), Size = new Size(640, 416), Visible = false };

            verifyIntro = new Label
            {
                Text =
                    $"Now close {processName} and open it again, loading your show or project the way you " +
                    "normally do. VariABLE OSC notices the restart by itself and re-attaches. When the " +
                    "values are on screen again, type each one exactly as the program shows it.",
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(0, 0),
                Size = new Size(640, 76)
            };

            restartStatus = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(0, 82),
                Size = new Size(640, 24)
            };

            rowsPanel = new Panel
            {
                Location = new Point(0, 112),
                Size = new Size(640, 130),
                AutoScroll = true
            };

            verifyButton = MakeButton("Check", 0, 252, 150);
            backToScanButton = MakeButton("Scan Again Instead", 160, 252, 190);
            continueButton = MakeButton("Continue to Step 3 >", 360, 252, 230);
            continueButton.Visible = false;

            resultLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(0, 294),
                Size = new Size(640, 122)
            };

            verifyButton.Click += (s, e) => RunCheck();

            backToScanButton.Click += (s, e) =>
            {
                if (mode == CheckMode.Confirm)
                {
                    ShowCheckPage(CheckMode.Recheck);
                }
                else
                {
                    ShowLearnPage();
                }
            };

            continueButton.Click += (s, e) => ShowCheckPage(CheckMode.Confirm);

            verifyPanel.Controls.Add(verifyIntro);
            verifyPanel.Controls.Add(restartStatus);
            verifyPanel.Controls.Add(rowsPanel);
            verifyPanel.Controls.Add(verifyButton);
            verifyPanel.Controls.Add(backToScanButton);
            verifyPanel.Controls.Add(continueButton);
            verifyPanel.Controls.Add(resultLabel);

            // ---------------- Footer ----------------

            saveButton = MakeButton("Save Session...", 20, 520, 160);
            saveButton.Visible = saveSession != null;
            saveButton.Click += (s, e) => saveSession?.Invoke(this);

            closeButton = MakeButton("Close", 560, 520, 100);
            closeButton.Click += (s, e) => Close();

            Controls.Add(title);
            Controls.Add(stepLabel);
            Controls.Add(learnPanel);
            Controls.Add(verifyPanel);
            Controls.Add(saveButton);
            Controls.Add(closeButton);

            PopulateVariableList();
            UpdateDepthHint();

            tick = new System.Windows.Forms.Timer { Interval = 500 };
            tick.Tick += (s, e) => OnTick();
            tick.Start();

            bool hasScan = invoker.PermanenceCandidates.Count > 0;

            bool lost =
                invoker.PermanentPaths.Count > 0 &&
                invoker.LastConsensus.Health == PermanenceHealth.Lost;

            if (invoker.PermanenceState == PermanenceState.Learned && hasScan)
            {
                ShowCheckPage(CheckMode.Restart);
            }
            else if (invoker.PermanenceState == PermanenceState.Provisional && invoker.PermanenceShortlist.Count > 0 && !lost)
            {
                ShowCheckPage(CheckMode.Confirm);
            }
            else if (hasScan && lost)
            {
                ShowCheckPage(CheckMode.Recheck);
            }
            else
            {
                ShowLearnPage();
            }

            CyberTheme.Apply(this);
            ApplyStatusColors();

            // Apply restyles every button as active; put the dimmed
            // states back.
            SetLearnRunning(false);

            if (onVerifyPage)
            {
                OnTick();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (scanning)
            {
                DialogResult stop = MessageBox.Show(
                    this,
                    "A scan is still running. Cancel it and close?",
                    "Scan In Progress",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (stop != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                scanCancel?.Cancel();
            }
            else if (Changed && candidates.Any(v =>
                v.PermanenceState == PermanenceState.Learned || v.PermanenceState == PermanenceState.Provisional))
            {
                DialogResult save = MessageBox.Show(
                    this,
                    "Your progress is stored with the variables, but only in memory so far.\n\n" +
                    "Save the session now so you can pick this up after restarting " +
                    $"{processName} (or the whole PC)?\n\n" +
                    "To finish later: load the session, open Make Permanent again and it continues " +
                    "where you left off.",
                    "Save Your Progress?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (save == DialogResult.Yes)
                {
                    saveSession?.Invoke(this);
                }
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tick.Stop();
                tick.Dispose();
                scanCancel?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Button MakeButton(string text, int x, int y, int width)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 32),
                Font = new Font("Segoe UI", 9.5f)
            };
        }

        // ------------------------------------------------------------
        // Pages
        // ------------------------------------------------------------

        private void ShowLearnPage()
        {
            onVerifyPage = false;
            learnPanel.Visible = true;
            verifyPanel.Visible = false;
            stepLabel.Text = "Step 1 of 3 - Scan: find every chain that leads to the variables";
            AcceptButton = null;
            PopulateVariableList();
            SetLearnRunning(false);
        }

        private void ShowCheckPage(CheckMode newMode)
        {
            mode = newMode;
            onVerifyPage = true;
            learnPanel.Visible = false;
            verifyPanel.Visible = true;
            continueButton.Visible = false;
            resultLabel.Text = "";
            AcceptButton = verifyButton;

            switch (mode)
            {
                case CheckMode.Confirm:
                    stepLabel.Text = "Step 3 of 3 - Confirm: prove the chains follow the value";
                    verifyIntro.Text =
                        "Almost there. A value like a cue number can match by coincidence, so now CHANGE it in " +
                        $"{processName} (for the active cue, go to a different cue) and type the NEW value for " +
                        "each variable. Only chains that follow the change are kept.";
                    verifyButton.Text = "Confirm";
                    backToScanButton.Text = "Back to Step 2";
                    break;

                case CheckMode.Recheck:
                    stepLabel.Text = "Re-check: recover these variables from the saved scan";
                    verifyIntro.Text =
                        $"This checks the whole saved scan again, so no new scan or restart is needed. Make sure " +
                        $"{processName} is running with your show loaded and the values showing (for the active " +
                        "cue, fire a cue first), then type each value exactly as it shows right now.";
                    verifyButton.Text = "Check";
                    backToScanButton.Text = "Scan Again Instead";
                    break;

                default:
                    stepLabel.Text = "Step 2 of 3 - Restart check: find the variables again after a restart";
                    verifyIntro.Text =
                        $"Now close {processName} and open it again, loading your show the way you normally do. " +
                        "VariABLE OSC notices the restart by itself and re-attaches. When the values are on " +
                        "screen (for the active cue, fire a cue first), type each one exactly as shown.";
                    verifyButton.Text = "Check";
                    backToScanButton.Text = "Scan Again Instead";
                    break;
            }

            BuildVerifyRows();
            OnTick();
        }

        private string DescribeVariable(MemoryVariable variable, out bool canRead)
        {
            object? value = variable.Scanner?.ReadValue(variable);
            canRead = value != null;

            string shown = value == null
                ? "(cannot read right now)"
                : variable.Type == DataType.String
                    ? DisplayText.Sanitize(value.ToString())
                    : value.ToString() ?? "";

            string tag = variable.PermanenceState switch
            {
                PermanenceState.Permanent => "  [already permanent]",
                PermanenceState.Provisional => "  [needs step 3]",
                PermanenceState.Learned => "  [scan stored]",
                _ => ""
            };

            return $"{variable.Name}   =   {shown}{tag}";
        }

        private void PopulateVariableList()
        {
            variablesList.Items.Clear();
            readable.Clear();

            foreach (MemoryVariable variable in candidates)
            {
                string text = DescribeVariable(variable, out bool canRead);
                readable.Add(canRead);

                bool preselect =
                    canRead &&
                    (ReferenceEquals(variable, invoker) ||
                     variable.PermanenceState == PermanenceState.None);

                variablesList.Items.Add(text, preselect);
            }
        }

        private void UpdateDepthHint()
        {
            depthHint.Text = depthCombo.SelectedIndex switch
            {
                0 => "Up to 3 pointer hops, fields up to 0x200 bytes in. A quick first try.",
                2 => "Up to 5 pointer hops, fields up to 0x800 bytes in. Can take minutes and use more memory.",
                _ => "Up to 4 pointer hops, fields up to 0x400 bytes in. Right for most programs."
            };
        }

        private void ApplyStatusColors()
        {
            // CyberTheme.Apply recolors every Label; reassert the ones that
            // carry meaning.
            stepLabel.ForeColor = CyberTheme.Muted;
            depthHint.ForeColor = CyberTheme.Muted;
        }

        // ------------------------------------------------------------
        // Step 1
        // ------------------------------------------------------------

        private void SetLearnRunning(bool running)
        {
            scanning = running;
            CyberTheme.SetButtonActive(scanButton, !running);
            CyberTheme.SetButtonActive(cancelScanButton, running);
            cancelScanButton.Visible = running;
            useSavedScanButton.Visible = !running && invoker.PermanenceCandidates.Count > 0;
            variablesList.Enabled = !running;
            depthCombo.Enabled = !running;
            CyberTheme.SetButtonActive(closeButton, true);
        }

        private async void StartScan()
        {
            if (scanning)
            {
                return;
            }

            List<MemoryVariable> chosen = new();

            for (int i = 0; i < variablesList.Items.Count; i++)
            {
                if (variablesList.GetItemChecked(i))
                {
                    chosen.Add(candidates[i]);
                }
            }

            if (chosen.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Tick at least one variable that can currently be read.",
                    "Nothing Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            MemoryScanner? scanner = chosen[0].Scanner;

            if (scanner == null || scanner.HasExited || chosen.Any(v => !ReferenceEquals(v.Scanner, scanner)))
            {
                MessageBox.Show(
                    this,
                    $"{processName} isn't running (or the variables are attached to different copies of it).",
                    "Can't Scan",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            ScanThoroughness thoroughness = depthCombo.SelectedIndex switch
            {
                0 => ScanThoroughness.Quick,
                2 => ScanThoroughness.Deep,
                _ => ScanThoroughness.Standard
            };

            PointerScanOptions options = PermanenceWorkflow.OptionsFor(thoroughness);

            scanCancel?.Dispose();
            scanCancel = new CancellationTokenSource();
            CancellationToken token = scanCancel.Token;

            progressBar.Value = 0;
            learnStatus.Text = "Starting...";
            SetLearnRunning(true);

            Progress<PointerScanProgress> progress = new(p =>
            {
                if (IsDisposed)
                {
                    return;
                }

                double overall = ((p.Level - 1) + p.LevelFraction) / p.MaxLevels;
                progressBar.Value = Math.Max(0, Math.Min(1000, (int)(overall * 1000)));

                learnStatus.Text =
                    $"Pass {p.Level} of {p.MaxLevels} - {(int)(p.LevelFraction * 100)}%\n" +
                    $"{p.NodesFound:N0} possible links, {p.StaticPathsFound:N0} complete chains so far";
            });

            List<LearnSummary>? summaries = null;
            string? failure = null;
            bool cancelled = false;

            try
            {
                summaries = await Task.Run(
                    () => PermanenceWorkflow.Learn(scanner, chosen, options, progress, token));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            if (IsDisposed)
            {
                return;
            }

            SetLearnRunning(false);
            progressBar.Value = 0;

            if (cancelled)
            {
                learnStatus.Text = "Scan cancelled. Nothing was changed.";
                return;
            }

            if (failure != null || summaries == null)
            {
                learnStatus.Text = "";

                MessageBox.Show(
                    this,
                    $"The scan failed: {failure}",
                    "Scan Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            Changed = true;
            ReportLearnResults(summaries);
        }

        private void ReportLearnResults(List<LearnSummary> summaries)
        {
            List<string> lines = new();
            bool anyNeedRestart = false;

            foreach (LearnSummary summary in summaries)
            {
                string name = summary.Variable.Name;

                if (summary.Variable.PermanenceState == PermanenceState.Permanent)
                {
                    lines.Add($"{name}: lives at a fixed spot in the program - permanent already, no restart check needed.");
                }
                else if (summary.CandidateCount == 0)
                {
                    lines.Add($"{name}: no chains found. {summary.Trail}");
                }
                else
                {
                    anyNeedRestart = true;
                    lines.Add($"{name}: {summary.CandidateCount:N0} candidate chains stored{(summary.Truncated ? " (best kept)" : "")}.");
                }
            }

            if (!anyNeedRestart)
            {
                learnStatus.Text = string.Join("\n", lines);
                PopulateVariableList();
                return;
            }

            MessageBox.Show(
                this,
                string.Join("\n", lines) +
                $"\n\nNext: close {processName} and open it again. VariABLE OSC will re-attach by itself, " +
                "and this wizard will ask you to confirm the values.",
                "Scan Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            ShowCheckPage(CheckMode.Restart);
        }

        // ------------------------------------------------------------
        // Steps 2 and 3 (and re-check) share one page
        // ------------------------------------------------------------

        private IEnumerable<MemoryVariable> PendingVariables()
        {
            switch (mode)
            {
                case CheckMode.Confirm:
                    return candidates.Where(v =>
                        v.PermanenceState == PermanenceState.Provisional && v.PermanenceShortlist.Count > 0);

                case CheckMode.Recheck:
                    return candidates.Where(v =>
                        v.PermanenceCandidates.Count > 0 &&
                        (ReferenceEquals(v, invoker) ||
                         (v.PermanentPaths.Count > 0 && v.LastConsensus.Health == PermanenceHealth.Lost)));

                default:
                    return candidates.Where(v =>
                        v.PermanenceCandidates.Count > 0 && v.PermanenceState == PermanenceState.Learned);
            }
        }

        private void BuildVerifyRows()
        {
            rowsPanel.Controls.Clear();
            rows.Clear();

            int y = 0;

            foreach (MemoryVariable variable in PendingVariables())
            {
                Label name = new Label
                {
                    Text = variable.Name,
                    Font = new Font("Segoe UI", 9.5f),
                    Location = new Point(0, y + 5),
                    Size = new Size(200, 22)
                };

                TextBox input = new TextBox
                {
                    Location = new Point(206, y + 2),
                    Size = new Size(240, 26),
                    Font = new Font("Segoe UI", 9.5f)
                };

                Label result = new Label
                {
                    Text = mode == CheckMode.Confirm && variable.PermanenceLastCheckValue.Length > 0
                        ? $"was: {variable.PermanenceLastCheckValue}"
                        : "",
                    Font = new Font("Segoe UI", 9),
                    Location = new Point(454, y + 5),
                    Size = new Size(180, 22)
                };

                rowsPanel.Controls.Add(name);
                rowsPanel.Controls.Add(input);
                rowsPanel.Controls.Add(result);

                rows.Add(new VerifyRow { Variable = variable, Input = input, Result = result });
                y += 34;
            }

            if (rows.Count == 0)
            {
                Label none = new Label
                {
                    Text = mode == CheckMode.Confirm
                        ? "Nothing is waiting for confirmation."
                        : "Nothing is waiting for a check.",
                    Font = new Font("Segoe UI", 9.5f),
                    Location = new Point(0, 4),
                    Size = new Size(620, 22)
                };

                rowsPanel.Controls.Add(none);
            }

            // Fresh controls were added after CyberTheme.Apply ran, so
            // restyle them now.
            CyberTheme.Apply(rowsPanel);
            rowsPanel.BackColor = CyberTheme.Page;

            foreach (VerifyRow row in rows)
            {
                if (row.Result.Text.StartsWith("was:", StringComparison.Ordinal))
                {
                    row.Result.ForeColor = CyberTheme.Muted;
                }
            }
        }

        private bool AllReady()
        {
            if (rows.Count == 0)
            {
                return false;
            }

            return mode == CheckMode.Restart
                ? rows.All(r => PermanenceWorkflow.HasRestartedSinceLearning(r.Variable))
                : rows.All(r => r.Variable.Scanner != null && !r.Variable.Scanner.HasExited);
        }

        private void OnTick()
        {
            if (!onVerifyPage)
            {
                return;
            }

            bool ready = AllReady();
            bool closedNow = scannerAtOpen != null && scannerAtOpen.HasExited;
            bool anyScannerGone = rows.Any(r => r.Variable.Scanner == null || r.Variable.Scanner.HasExited);

            if (rows.Count == 0)
            {
                restartStatus.Text = "";
                restartStatus.ForeColor = CyberTheme.Muted;
            }
            else if (ready)
            {
                restartStatus.Text = mode switch
                {
                    CheckMode.Confirm => $"Change the value in {processName}, then enter the NEW value for each variable:",
                    CheckMode.Recheck => $"{processName} is running - enter what each variable shows right now:",
                    _ => $"{processName} is running again - enter what each variable shows now:"
                };

                restartStatus.ForeColor = CyberTheme.Accent;
            }
            else if (closedNow || anyScannerGone)
            {
                restartStatus.Text = $"{processName} has closed. Waiting for it to start again...";
                restartStatus.ForeColor = CyberTheme.Warning;
            }
            else
            {
                restartStatus.Text = $"Waiting for you to close and reopen {processName}...";
                restartStatus.ForeColor = CyberTheme.Warning;
            }

            foreach (VerifyRow row in rows)
            {
                if (!row.Done)
                {
                    row.Input.Enabled = ready;
                }
            }

            CyberTheme.SetButtonActive(verifyButton, ready);
        }

        private void RunCheck()
        {
            if (!AllReady())
            {
                return;
            }

            List<string> lines = new();
            int attempted = 0;
            int permanent = 0;
            int needStep3 = 0;

            Cursor? previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;

            try
            {
                foreach (VerifyRow row in rows)
                {
                    string text = row.Input.Text.Trim();

                    if (text.Length == 0)
                    {
                        continue;
                    }

                    object? expected;

                    if (row.Variable.Type == DataType.String)
                    {
                        expected = text;
                    }
                    else if (!ValueParsing.TryParseValue(text, row.Variable.Type, out expected, out string error))
                    {
                        row.Result.Text = "not a valid value";
                        row.Result.ForeColor = CyberTheme.Danger;
                        lines.Add($"{row.Variable.Name}: {error}");
                        continue;
                    }

                    attempted++;

                    VerifyOutcome outcome = mode == CheckMode.Confirm
                        ? PermanenceWorkflow.Confirm(row.Variable, expected!, text)
                        : PermanenceWorkflow.RestartCheck(row.Variable, expected!, text);

                    if (outcome.Succeeded)
                    {
                        Changed = true;
                        row.Done = true;

                        if (outcome.NewState == PermanenceState.Permanent)
                        {
                            permanent++;
                            row.Result.Text = $"Permanent - {outcome.Survivors} chain(s)";
                            row.Result.ForeColor = CyberTheme.Accent;
                            row.Input.Enabled = false;
                        }
                        else
                        {
                            needStep3++;
                            row.Result.Text = $"Matched {outcome.Survivors:N0} - confirm next";
                            row.Result.ForeColor = CyberTheme.Warning;
                            row.Input.Enabled = false;
                        }
                    }
                    else
                    {
                        row.Result.Text = mode == CheckMode.Confirm ? "Didn't follow the change" : "No chain matched";
                        row.Result.ForeColor = CyberTheme.Danger;
                    }

                    lines.Add($"{row.Variable.Name}: {outcome.Message}");
                }
            }
            finally
            {
                Cursor.Current = previous;
            }

            if (attempted == 0 && lines.Count == 0)
            {
                resultLabel.Text = "Type the value each variable shows, then press the button.";
                return;
            }

            resultLabel.Text = string.Join("\n", lines);

            if (mode != CheckMode.Confirm && needStep3 > 0)
            {
                resultLabel.Text += "\n\nNext: step 3. Change the value in the program, then continue.";
                continueButton.Visible = true;
                CyberTheme.SetButtonActive(continueButton, true);
            }

            if (attempted > 0 && permanent == attempted)
            {
                resultLabel.Text += "\n\nAll done - these variables will now find themselves after every restart.";
                closeButton.Text = "Done";
            }
        }
    }
}
