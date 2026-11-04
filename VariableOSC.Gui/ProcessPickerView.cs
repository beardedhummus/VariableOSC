using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// Step 2 of the New Session flow: pick which running process to scan.
    /// Mirrors the console ProcessPicker's behavior (windowed apps only by
    /// default, search filter, refresh) using the shared ProcessCatalog
    /// from Core, so both front ends list/filter processes identically.
    /// </summary>
    public class ProcessPickerView : UserControl
    {
        private readonly TextBox searchBox;
        private readonly CheckBox showAllCheckBox;
        private readonly DataGridView grid;
        private readonly Button selectButton;

        private List<ProcessEntry> snapshot = new();

        public event Action? BackRequested;

        public event Action<ProcessEntry>? ProcessSelected;

        public ProcessPickerView()
        {
            AutoScaleMode = AutoScaleMode.None;

            // A UserControl's default constructed size is 150x150 - since
            // every child below is added (and their Anchor margins locked
            // in) BEFORE MainForm docks this view to its real ~800x680
            // size, bottom-anchored controls positioned well past y=150
            // would otherwise capture a huge negative margin and end up
            // nowhere near the actual bottom edge once docked. Setting the
            // real target size first makes the anchors correct from the
            // start.
            Size = new Size(800, 680);

            Dock = DockStyle.Fill;

            Label title = new Label
            {
                Text = "Select a Process",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 20)
            };

            Label searchLabel = new Label
            {
                Text = "Search:",
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = true,
                Location = new Point(24, 66)
            };

            searchBox = new TextBox
            {
                Location = new Point(80, 62),
                Size = new Size(260, 24),
                Font = new Font("Segoe UI", 10)
            };

            searchBox.TextChanged += (s, e) => ApplyFilter();

            showAllCheckBox = new CheckBox
            {
                Text = "Show all processes (not just windowed apps)",
                Location = new Point(360, 64),
                Size = new Size(320, 24),
                Font = new Font("Segoe UI", 9.5f)
            };

            showAllCheckBox.CheckedChanged += (s, e) => ApplyFilter();

            Button refreshButton = new Button
            {
                Text = "Refresh",
                Location = new Point(690, 60),
                Size = new Size(86, 28),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            refreshButton.Click += (s, e) => TakeSnapshotAndFilter();

            grid = new DataGridView
            {
                Location = new Point(24, 98),
                Size = new Size(752, 398),
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
                Name = "Name",
                HeaderText = "Process",
                FillWeight = 90
            });

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Pid",
                HeaderText = "PID",
                FillWeight = 40
            });

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "WindowTitle",
                HeaderText = "Window Title",
                FillWeight = 200
            });

            grid.CellDoubleClick += (s, e) => TrySelectCurrentRow();
            grid.SelectionChanged += (s, e) => UpdateSelectButtonState();

            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    // DataGridView's default Enter behavior just moves the
                    // current cell down a row - intercept it so Enter
                    // confirms the selection instead, matching every other
                    // Enter-to-confirm control in the app.
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    TrySelectCurrentRow();
                }
            };

            Button backButton = new Button
            {
                Text = "< Back",
                Location = new Point(24, 508),
                Size = new Size(100, 34),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            backButton.Click += (s, e) => BackRequested?.Invoke();

            selectButton = new Button
            {
                Text = "Select",
                Location = new Point(676, 508),
                Size = new Size(100, 34),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = false
            };

            selectButton.Click += (s, e) => TrySelectCurrentRow();

            Controls.Add(title);
            Controls.Add(searchLabel);
            Controls.Add(searchBox);
            Controls.Add(showAllCheckBox);
            Controls.Add(refreshButton);
            Controls.Add(grid);
            Controls.Add(backButton);
            Controls.Add(selectButton);

            TakeSnapshotAndFilter();

            CyberTheme.Apply(this);
        }

        private void TakeSnapshotAndFilter()
        {
            snapshot = ProcessCatalog.TakeSnapshot();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            List<ProcessEntry> visible = ProcessCatalog.Filter(
                snapshot,
                !showAllCheckBox.Checked,
                searchBox.Text);

            grid.Rows.Clear();

            foreach (ProcessEntry entry in visible)
            {
                int rowIndex = grid.Rows.Add(entry.Name, entry.Id, entry.WindowTitle);
                grid.Rows[rowIndex].Tag = entry;
            }

            UpdateSelectButtonState();
        }

        private void UpdateSelectButtonState()
        {
            CyberTheme.SetButtonActive(selectButton, grid.SelectedRows.Count > 0);
        }

        private void TrySelectCurrentRow()
        {
            if (grid.SelectedRows.Count == 0)
            {
                return;
            }

            if (grid.SelectedRows[0].Tag is ProcessEntry entry)
            {
                ProcessSelected?.Invoke(entry);
            }
        }
    }
}
