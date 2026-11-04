using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// Step 3: search the target process's memory and manage the resulting
    /// candidates. Unlike the console's menu-driven flow (pick a category,
    /// then a value, then loop through numbered options), this is one
    /// persistent panel - a type selector, a value box whose action button
    /// switches between "First Scan" and "Next Scan", and a candidate grid
    /// that keeps its values live-updating in the background. That's the
    /// natural GUI shape for this kind of tool and keeps the whole step on
    /// one screen instead of a chain of dialogs.
    /// </summary>
    public class ScanView : UserControl
    {
        private const int MaxDisplayedCandidates = 2000;
        private const int MaxLiveUpdateCandidates = 300;

        private readonly MemoryScanner scanner;
        private readonly string processName;
        private readonly MemoryVariable? replaceTarget;

        private readonly ComboBox categoryCombo;
        private readonly CheckBox advancedCheckBox;
        private readonly ComboBox advancedTypeCombo;

        private readonly CheckBox caseSensitiveCheckBox;
        private readonly CheckBox nullTerminatedCheckBox;
        private readonly CheckBox fixedLengthCheckBox;
        private readonly NumericUpDown fixedLengthValue;

        private readonly TextBox valueBox;
        private readonly Button scanButton;
        private readonly Button newScanButton;

        private readonly DataGridView grid;
        private readonly Label statusLabel;
        private readonly Button saveButton;

        private readonly System.Windows.Forms.Timer refreshTimer;

        private List<NumberMatch>? numberCandidates;
        private List<IntPtr>? simpleCandidates;
        private DataType simpleType;
        private List<StringMatch>? stringCandidates;

        private bool HasActiveScan =>
            numberCandidates != null || simpleCandidates != null || stringCandidates != null;

        public event Action? BackRequested;

        public event Action<MemoryVariable>? VariableSaved;

        /// <summary>
        /// Fired instead of VariableSaved when this view was opened in
        /// Replace Target mode (see the replaceTarget constructor param) -
        /// the console's "Replace Target" search, ported to the GUI.
        /// </summary>
        public event Action<MemoryVariable>? TargetReplaced;

        public ScanView(
            MemoryScanner scanner,
            string processName,
            int processId,
            MemoryVariable? replaceTarget = null)
        {
            this.scanner = scanner;
            this.processName = processName;
            this.replaceTarget = replaceTarget;

            AutoScaleMode = AutoScaleMode.None;

            // See ProcessPickerView for why this matters: without it,
            // every bottom-anchored control below captures its Anchor
            // margin against a UserControl's 150x150 default constructed
            // size instead of the real ~800x680 docked size.
            Size = new Size(800, 680);

            Dock = DockStyle.Fill;

            Label header = new Label
            {
                Text = replaceTarget != null
                    ? $"Replacing target for \"{replaceTarget.Name}\": {processName} (PID {processId})"
                    : $"Scanning: {processName} (PID {processId})",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 18)
            };

            Button backButton = new Button
            {
                Text = "< Change Process",
                Location = new Point(600, 16),
                Size = new Size(152, 30),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            backButton.Click += (s, e) => BackRequested?.Invoke();

            Label typeLabel = new Label
            {
                Text = "Type:",
                AutoSize = true,
                Location = new Point(24, 62),
                Font = new Font("Segoe UI", 9.5f)
            };

            categoryCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(70, 58),
                Size = new Size(110, 24),
                Font = new Font("Segoe UI", 9.5f)
            };

            categoryCombo.Items.AddRange(new object[] { "Numbers", "Bool", "String" });
            categoryCombo.SelectedIndex = 0;
            categoryCombo.SelectedIndexChanged += (s, e) => OnModeChanged();

            advancedCheckBox = new CheckBox
            {
                Text = "Advanced (specific type)",
                Location = new Point(190, 60),
                Size = new Size(180, 24),
                Font = new Font("Segoe UI", 9.5f)
            };

            advancedCheckBox.CheckedChanged += (s, e) => OnModeChanged();

            advancedTypeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(376, 58),
                Size = new Size(100, 24),
                Font = new Font("Segoe UI", 9.5f)
            };

            advancedTypeCombo.Items.AddRange(new object[]
            {
                DataType.Byte, DataType.Int16, DataType.Int32,
                DataType.Int64, DataType.Float, DataType.Double
            });

            advancedTypeCombo.SelectedIndex = 2; // Int32 - the most common case
            advancedTypeCombo.SelectedIndexChanged += (s, e) => ResetScan();

            caseSensitiveCheckBox = new CheckBox
            {
                Text = "Case sensitive",
                Checked = true,
                Location = new Point(190, 60),
                Size = new Size(120, 24),
                Font = new Font("Segoe UI", 9.5f)
            };

            caseSensitiveCheckBox.CheckedChanged += (s, e) => ResetScan();

            nullTerminatedCheckBox = new CheckBox
            {
                Text = "Null-terminated",
                Location = new Point(310, 60),
                Size = new Size(130, 24),
                Font = new Font("Segoe UI", 9.5f)
            };

            nullTerminatedCheckBox.CheckedChanged += (s, e) => ResetScan();

            fixedLengthCheckBox = new CheckBox
            {
                Text = "Fixed length:",
                Location = new Point(444, 60),
                Size = new Size(100, 24),
                Font = new Font("Segoe UI", 9.5f)
            };

            fixedLengthValue = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 4096,
                Value = 32,
                Location = new Point(542, 58),
                Size = new Size(60, 24),
                Font = new Font("Segoe UI", 9.5f),
                Enabled = false
            };

            fixedLengthCheckBox.CheckedChanged += (s, e) =>
            {
                fixedLengthValue.Enabled = fixedLengthCheckBox.Checked;
                ResetScan();
            };

            fixedLengthValue.ValueChanged += (s, e) => ResetScan();

            Label valueLabel = new Label
            {
                Text = "Value:",
                AutoSize = true,
                Location = new Point(24, 98),
                Font = new Font("Segoe UI", 9.5f)
            };

            valueBox = new TextBox
            {
                Location = new Point(70, 94),
                Size = new Size(300, 24),
                Font = new Font("Segoe UI", 10)
            };

            valueBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ExecuteScan();
                }
            };

            scanButton = new Button
            {
                Text = "First Scan",
                Location = new Point(380, 92),
                Size = new Size(110, 28),
                Font = new Font("Segoe UI", 9.5f)
            };

            scanButton.Click += (s, e) => ExecuteScan();

            newScanButton = new Button
            {
                Text = "New Scan",
                Location = new Point(496, 92),
                Size = new Size(96, 28),
                Font = new Font("Segoe UI", 9.5f),
                Enabled = false
            };

            newScanButton.Click += (s, e) => ResetScan();

            grid = new DataGridView
            {
                Location = new Point(24, 132),
                Size = new Size(752, 380),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Address",
                HeaderText = "Address",
                FillWeight = 90
            });

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Type",
                HeaderText = "Type",
                FillWeight = 60
            });

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Value",
                HeaderText = "Value",
                FillWeight = 120
            });

            statusLabel = new Label
            {
                Text = "Pick a type and enter a value, then First Scan.",
                AutoSize = true,
                Location = new Point(24, 520),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.DimGray,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            Button backToPickerButton = new Button
            {
                Text = "< Back",
                Location = new Point(24, 546),
                Size = new Size(100, 34),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            backToPickerButton.Click += (s, e) => BackRequested?.Invoke();

            saveButton = new Button
            {
                Text = replaceTarget != null ? "Use Selected to Replace Target" : "Save Selected as Variable",
                Location = new Point(526, 546),
                Size = new Size(250, 34),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = false
            };

            saveButton.Click += (s, e) => SaveSelectedCandidate();

            grid.SelectionChanged += (s, e) =>
            {
                CyberTheme.SetButtonActive(saveButton, grid.SelectedRows.Count > 0);
            };

            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    // Same fix as the process picker's grid - Enter would
                    // otherwise just move the current cell down a row.
                    e.Handled = true;
                    e.SuppressKeyPress = true;

                    if (grid.SelectedRows.Count > 0)
                    {
                        SaveSelectedCandidate();
                    }
                }
            };

            Controls.Add(header);
            Controls.Add(backButton);
            Controls.Add(typeLabel);
            Controls.Add(categoryCombo);
            Controls.Add(advancedCheckBox);
            Controls.Add(advancedTypeCombo);
            Controls.Add(caseSensitiveCheckBox);
            Controls.Add(nullTerminatedCheckBox);
            Controls.Add(fixedLengthCheckBox);
            Controls.Add(fixedLengthValue);
            Controls.Add(valueLabel);
            Controls.Add(valueBox);
            Controls.Add(scanButton);
            Controls.Add(newScanButton);
            Controls.Add(grid);
            Controls.Add(statusLabel);
            Controls.Add(backToPickerButton);
            Controls.Add(saveButton);

            refreshTimer = new System.Windows.Forms.Timer { Interval = 300 };
            refreshTimer.Tick += (s, e) => RefreshLiveValues();
            refreshTimer.Start();

            OnModeChanged();

            CyberTheme.Apply(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
                StopLoadingAnimation();
            }

            base.Dispose(disposing);
        }

        // ------------------------------------------------------------
        // Mode handling
        // ------------------------------------------------------------

        private bool IsNumbersMode => categoryCombo.SelectedIndex == 0;
        private bool IsBoolMode => categoryCombo.SelectedIndex == 1;
        private bool IsStringMode => categoryCombo.SelectedIndex == 2;

        private void OnModeChanged()
        {
            advancedCheckBox.Visible = IsNumbersMode;
            advancedTypeCombo.Visible = IsNumbersMode && advancedCheckBox.Checked;

            bool stringOptionsVisible = IsStringMode;
            caseSensitiveCheckBox.Visible = stringOptionsVisible;
            nullTerminatedCheckBox.Visible = stringOptionsVisible;
            fixedLengthCheckBox.Visible = stringOptionsVisible;
            fixedLengthValue.Visible = stringOptionsVisible;

            ResetScan();
        }

        private void ResetScan()
        {
            numberCandidates = null;
            simpleCandidates = null;
            stringCandidates = null;

            grid.Rows.Clear();
            scanButton.Text = "First Scan";
            CyberTheme.SetButtonActive(newScanButton, false);
            CyberTheme.SetButtonActive(saveButton, false);
            statusLabel.Text = "Pick a type and enter a value, then First Scan.";
        }

        // ------------------------------------------------------------
        // Scanning
        // ------------------------------------------------------------

        // Scanning walks the target's entire address space and can take a
        // real, visible amount of time - it used to run straight on the UI
        // thread, freezing the window with no feedback. Task.Run moves the
        // actual scan off the UI thread; isScanning guards against a
        // second scan starting while one's already running.
        private bool isScanning;

        private async void ExecuteScan()
        {
            if (isScanning)
            {
                return;
            }

            bool isFirstScan = !HasActiveScan;

            isScanning = true;
            CyberTheme.SetButtonActive(scanButton, false);
            CyberTheme.SetButtonActive(newScanButton, false);
            StartLoadingAnimation(isFirstScan ? "Scanning" : "Narrowing");

            try
            {
                if (IsNumbersMode && !advancedCheckBox.Checked)
                {
                    await ExecuteNumbersAllScan(isFirstScan);
                }
                else if (IsNumbersMode)
                {
                    await ExecuteSimpleScan((DataType)advancedTypeCombo.SelectedItem!, isFirstScan);
                }
                else if (IsBoolMode)
                {
                    await ExecuteSimpleScan(DataType.Bool, isFirstScan);
                }
                else
                {
                    await ExecuteStringScan(isFirstScan);
                }
            }
            finally
            {
                isScanning = false;
                StopLoadingAnimation();
                CyberTheme.SetButtonActive(scanButton, true);
            }
        }

        private async Task ExecuteNumbersAllScan(bool isFirstScan)
        {
            if (!ValueParsing.TryParseAnyNumber(valueBox.Text, out Dictionary<DataType, object> valuesByType, out string error))
            {
                statusLabel.Text = error;
                return;
            }

            numberCandidates = isFirstScan
                ? await Task.Run(() => scanner.FirstScanAnyNumber(valuesByType))
                : await Task.Run(() => scanner.NextScanAnyNumber(numberCandidates!, valuesByType));

            PopulateNumberGrid(numberCandidates);
            OnScanCompleted(numberCandidates.Count);
        }

        private async Task ExecuteSimpleScan(DataType type, bool isFirstScan)
        {
            if (!ValueParsing.TryParseValue(valueBox.Text, type, out object? value, out string error))
            {
                statusLabel.Text = error;
                return;
            }

            simpleType = type;

            simpleCandidates = isFirstScan
                ? await Task.Run(() => scanner.FirstScan(value!, type))
                : await Task.Run(() => scanner.NextScan(simpleCandidates!, value!, type));

            PopulateSimpleGrid(simpleCandidates, type);
            OnScanCompleted(simpleCandidates.Count);
        }

        private async Task ExecuteStringScan(bool isFirstScan)
        {
            string text = valueBox.Text;

            if (string.IsNullOrEmpty(text))
            {
                statusLabel.Text = "You must enter some text.";
                return;
            }

            StringSearchOptions options = new StringSearchOptions
            {
                CaseSensitive = caseSensitiveCheckBox.Checked,
                NullTerminated = nullTerminatedCheckBox.Checked,
                FixedLength = fixedLengthCheckBox.Checked,
                FixedLengthChars = (int)fixedLengthValue.Value
            };

            stringCandidates = isFirstScan
                ? await Task.Run(() => scanner.FirstScanString(text, options))
                : await Task.Run(() => scanner.NextScanString(stringCandidates!, text, options));

            PopulateStringGrid(stringCandidates);
            OnScanCompleted(stringCandidates.Count);
        }

        private System.Windows.Forms.Timer? loadingAnimationTimer;
        private int loadingDotCount;

        private void StartLoadingAnimation(string baseText)
        {
            loadingDotCount = 0;
            statusLabel.Text = baseText;

            loadingAnimationTimer = new System.Windows.Forms.Timer { Interval = 400 };

            loadingAnimationTimer.Tick += (s, e) =>
            {
                loadingDotCount = (loadingDotCount % 3) + 1;
                statusLabel.Text = baseText + new string('.', loadingDotCount);
            };

            loadingAnimationTimer.Start();
        }

        private void StopLoadingAnimation()
        {
            loadingAnimationTimer?.Stop();
            loadingAnimationTimer?.Dispose();
            loadingAnimationTimer = null;
        }

        private void OnScanCompleted(int count)
        {
            scanButton.Text = "Next Scan";
            CyberTheme.SetButtonActive(newScanButton, true);

            statusLabel.Text = count > MaxLiveUpdateCandidates
                ? $"{count} candidate(s) - too many to live-update. Narrow your search to see live values."
                : $"{count} candidate(s).";
        }

        // ------------------------------------------------------------
        // Grid population
        // ------------------------------------------------------------

        private void PopulateNumberGrid(List<NumberMatch> candidates)
        {
            grid.Rows.Clear();

            foreach (NumberMatch candidate in Truncate(candidates))
            {
                object? value = scanner.ReadValue(candidate.Address, candidate.Type);

                int rowIndex = grid.Rows.Add(
                    $"0x{candidate.Address.ToInt64():X}",
                    candidate.Type.ToString(),
                    value?.ToString() ?? "-");

                grid.Rows[rowIndex].Tag = candidate;
            }
        }

        private void PopulateSimpleGrid(List<IntPtr> candidates, DataType type)
        {
            grid.Rows.Clear();

            foreach (IntPtr address in Truncate(candidates))
            {
                object? value = scanner.ReadValue(address, type);

                int rowIndex = grid.Rows.Add(
                    $"0x{address.ToInt64():X}",
                    type.ToString(),
                    value?.ToString() ?? "-");

                grid.Rows[rowIndex].Tag = address;
            }
        }

        private void PopulateStringGrid(List<StringMatch> candidates)
        {
            grid.Rows.Clear();

            foreach (StringMatch candidate in Truncate(candidates))
            {
                string? value = scanner.ReadString(
                    candidate.Address,
                    candidate.Encoding,
                    Math.Max(candidate.MatchedCharLength, 32));

                int rowIndex = grid.Rows.Add(
                    $"0x{candidate.Address.ToInt64():X}",
                    $"String ({candidate.Encoding})",
                    value != null ? DisplayText.Sanitize(value) : "-");

                grid.Rows[rowIndex].Tag = candidate;
            }
        }

        private static IEnumerable<T> Truncate<T>(List<T> source)
        {
            int count = Math.Min(source.Count, MaxDisplayedCandidates);

            for (int i = 0; i < count; i++)
            {
                yield return source[i];
            }
        }

        // ------------------------------------------------------------
        // Live value refresh
        // ------------------------------------------------------------

        private void RefreshLiveValues()
        {
            if (!HasActiveScan || grid.Rows.Count == 0 || grid.Rows.Count > MaxLiveUpdateCandidates)
            {
                return;
            }

            foreach (DataGridViewRow row in grid.Rows)
            {
                string display = row.Tag switch
                {
                    NumberMatch nm => scanner.ReadValue(nm.Address, nm.Type)?.ToString() ?? "-",
                    StringMatch sm => DisplayText.Sanitize(scanner.ReadString(sm.Address, sm.Encoding, Math.Max(sm.MatchedCharLength, 32))) is { Length: > 0 } sanitized ? sanitized : "-",
                    IntPtr addr => scanner.ReadValue(addr, simpleType)?.ToString() ?? "-",
                    _ => "-"
                };

                row.Cells["Value"].Value = display;
            }
        }

        // ------------------------------------------------------------
        // Saving a candidate
        // ------------------------------------------------------------

        private void SaveSelectedCandidate()
        {
            if (grid.SelectedRows.Count == 0)
            {
                return;
            }

            object? tag = grid.SelectedRows[0].Tag;

            if (replaceTarget != null)
            {
                ApplyReplacement(tag);
                return;
            }

            using TextPromptDialog dialog = new TextPromptDialog(
                "Save Variable",
                "Name this variable:");

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value))
            {
                return;
            }

            MemoryVariable? variable = tag switch
            {
                NumberMatch nm => new MemoryVariable(dialog.Value, nm.Address, nm.Type)
                {
                    Scanner = scanner,
                    SourceProcessName = processName
                },

                IntPtr addr => new MemoryVariable(dialog.Value, addr, simpleType)
                {
                    Scanner = scanner,
                    SourceProcessName = processName
                },

                StringMatch sm => new MemoryVariable(dialog.Value, sm.Address, DataType.String)
                {
                    Scanner = scanner,
                    SourceProcessName = processName,
                    StringEncoding = sm.Encoding,
                    StringLength = fixedLengthCheckBox.Checked
                        ? (int)fixedLengthValue.Value
                        : Math.Max(sm.MatchedCharLength, 32)
                },

                _ => null
            };

            if (variable != null)
            {
                VariableSaved?.Invoke(variable);
            }
        }

        /// <summary>
        /// Replace Target mode: point the existing variable at whatever
        /// was just selected instead of creating a new one. Name and
        /// wherever the variable is referenced (savedVariables / a job's
        /// Items) stay untouched - only Address/Type/PointerPath/string
        /// metadata change, matching the console's ReplaceVariableTarget.
        /// </summary>
        private void ApplyReplacement(object? tag)
        {
            if (replaceTarget == null)
            {
                return;
            }

            switch (tag)
            {
                case NumberMatch nm:
                    replaceTarget.Type = nm.Type;
                    replaceTarget.Address = nm.Address;
                    replaceTarget.PointerPath = null;
                    break;

                case IntPtr addr:
                    replaceTarget.Type = simpleType;
                    replaceTarget.Address = addr;
                    replaceTarget.PointerPath = null;
                    break;

                case StringMatch sm:
                    replaceTarget.Type = DataType.String;
                    replaceTarget.Address = sm.Address;
                    replaceTarget.StringEncoding = sm.Encoding;
                    replaceTarget.StringLength = fixedLengthCheckBox.Checked
                        ? (int)fixedLengthValue.Value
                        : Math.Max(sm.MatchedCharLength, 32);
                    replaceTarget.PointerPath = null;
                    break;

                default:
                    return;
            }

            // The learned chains led to the OLD location; keeping them
            // would drag the variable straight back to it.
            replaceTarget.ClearPermanence();

            TargetReplaced?.Invoke(replaceTarget);
        }
    }
}
