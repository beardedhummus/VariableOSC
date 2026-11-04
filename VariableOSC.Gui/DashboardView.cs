using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// Step 4: manage everything scanned so far - standalone saved
    /// variables and OSC broadcasts/groups, mirroring the console's Saved
    /// Variables + OSC Broadcasts menus as one screen with two tabs.
    /// Operates directly on the shared savedVariables/broadcastManager
    /// state (same pattern as the console's Program.cs) rather than
    /// round-tripping through events for every micro-action.
    /// </summary>
    public class DashboardView : UserControl
    {
        private readonly List<MemoryVariable> savedVariables;
        private readonly OscBroadcastManager broadcastManager;

        private readonly DataGridView variablesGrid;
        private readonly DataGridView jobsGrid;
        private readonly DataGridView membersGrid;
        private readonly Button removeMemberButton;
        private readonly Button changeAddressButton;
        private readonly Button renameMemberVarButton;
        private readonly Button replaceMemberVarButton;
        private readonly Button convertMemberPointerButton;
        private readonly Button startStopButton;
        private readonly Button renameJobButton;
        private readonly Button deleteJobButton;
        private readonly Button sendIndividuallyButton;
        private readonly Button addToBroadcastButton;
        private readonly Button createBroadcastButton;
        private readonly Button convertVarPointerButton;
        private readonly Button makePermanentVarButton;
        private readonly Button makePermanentMemberButton;
        private readonly Label reconnectLabel;
        private readonly Button renameVarButton;
        private readonly Button replaceVarButton;
        private readonly Button removeVarButton;

        private readonly System.Windows.Forms.Timer refreshTimer;

        public event Action? NewScanRequested;

        public event Action? BackToWelcomeRequested;

        /// <summary>
        /// Raised with the variable to re-target when the user picks
        /// Replace Target - Program.cs handles it by opening ScanView in
        /// replace mode against that variable's existing Scanner.
        /// </summary>
        public event Action<MemoryVariable>? ReplaceTargetRequested;

        public DashboardView(List<MemoryVariable> savedVariables, OscBroadcastManager broadcastManager)
        {
            this.savedVariables = savedVariables;
            this.broadcastManager = broadcastManager;

            AutoScaleMode = AutoScaleMode.None;
            Size = new Size(800, 680);
            Dock = DockStyle.Fill;

            Label title = new Label
            {
                Text = "Saved Variables & Broadcasts",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 18)
            };

            Button newScanButton = new Button
            {
                Text = "New Scan",
                Location = new Point(546, 16),
                Size = new Size(110, 30),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            newScanButton.Click += (s, e) => NewScanRequested?.Invoke();

            Button saveSessionButton = new Button
            {
                Text = "Save Session",
                Location = new Point(662, 16),
                Size = new Size(114, 30),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            saveSessionButton.Click += (s, e) => SaveSession();

            // TabControl draws its own native border using OS visual
            // styles, which ignores BackColor/theme entirely and rendered
            // as a light gray/white frame against the dark skin. Wrapping
            // it in a Panel with a small Padding and a dark BackColor is a
            // reliable way to get a themed border regardless of what the
            // TabControl draws for itself: the Padding is the only part of
            // the wrapper left uncovered once the TabControl docks to
            // Fill, so it reads as a colored frame around it.
            Panel tabFrame = new Panel
            {
                Location = new Point(24, 56),
                Size = new Size(752, 574),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Padding = new Padding(2),
                BackColor = CyberTheme.FrameDark
            };

            TabControl tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f)
            };

            // Same fix as MainForm.ShowView's views: a TabPage's default
            // constructed size is small, and every bottom-anchored button
            // below is added to it before it's docked to its real size
            // inside the TabControl - set the real target size first so
            // those anchors capture correct margins.
            TabPage variablesTab = new TabPage("Saved Variables") { Size = new Size(744, 545) };
            TabPage broadcastsTab = new TabPage("Broadcasts") { Size = new Size(744, 545) };

            // --- Saved Variables tab ---

            variablesGrid = new DataGridView
            {
                Location = new Point(10, 10),
                Size = new Size(722, 420),
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

            variablesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", FillWeight = 80 });
            variablesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", FillWeight = 45 });
            variablesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Process", HeaderText = "Process", FillWeight = 65 });
            variablesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Permanence", HeaderText = "Permanence", FillWeight = 105 });
            variablesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 80 });

            sendIndividuallyButton = MakeButton("Send Individually", 10, 440, 170);
            addToBroadcastButton = MakeButton("Add to Broadcast", 190, 440, 170);
            createBroadcastButton = MakeButton("Create New Broadcast", 370, 440, 170);
            convertVarPointerButton = MakeButton("Convert to Pointer Path", 550, 440, 182);

            makePermanentVarButton = MakeButton("Make Permanent...", 550, 478, 182);
            renameVarButton = MakeButton("Rename", 10, 478, 170);
            replaceVarButton = MakeButton("Replace Target", 190, 478, 170);
            removeVarButton = MakeButton("Remove", 370, 478, 170);

            sendIndividuallyButton.Click += (s, e) => SendSelectedIndividually();
            addToBroadcastButton.Click += (s, e) => AddSelectedToExistingBroadcast();
            createBroadcastButton.Click += (s, e) => CreateBroadcastWithSelected();
            convertVarPointerButton.Click += (s, e) => ConvertToPointerPath(GetSelectedVariable());
            makePermanentVarButton.Click += (s, e) => MakePermanent(GetSelectedVariable());
            renameVarButton.Click += (s, e) => RenameVariable(GetSelectedVariable());
            replaceVarButton.Click += (s, e) => RequestReplaceTarget(GetSelectedVariable());
            removeVarButton.Click += (s, e) => RemoveSelectedVariable();

            variablesGrid.SelectionChanged += (s, e) => UpdateVariableButtonState();

            variablesGrid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            variablesTab.Controls.Add(variablesGrid);
            variablesTab.Controls.Add(sendIndividuallyButton);
            variablesTab.Controls.Add(addToBroadcastButton);
            variablesTab.Controls.Add(createBroadcastButton);
            variablesTab.Controls.Add(convertVarPointerButton);
            variablesTab.Controls.Add(renameVarButton);
            variablesTab.Controls.Add(replaceVarButton);
            variablesTab.Controls.Add(removeVarButton);
            variablesTab.Controls.Add(makePermanentVarButton);

            // --- Broadcasts tab ---

            Label jobsLabel = new Label
            {
                Text = "Broadcasts",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 8)
            };

            jobsGrid = new DataGridView
            {
                Location = new Point(10, 32),
                Size = new Size(722, 150),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
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

            jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", FillWeight = 90 });
            jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Kind", FillWeight = 50 });
            jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Destination", HeaderText = "Destination", FillWeight = 90 });
            jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 60 });
            jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Members", HeaderText = "Members", FillWeight = 50 });

            jobsGrid.SelectionChanged += (s, e) => RefreshMembersGrid();

            jobsGrid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            startStopButton = MakeButton("Start / Stop", 10, 188, 140);
            renameJobButton = MakeButton("Rename Broadcast", 160, 188, 150);
            deleteJobButton = MakeButton("Delete Broadcast", 320, 188, 150);

            startStopButton.Click += (s, e) => ToggleSelectedJob();
            renameJobButton.Click += (s, e) => RenameSelectedJob();
            deleteJobButton.Click += (s, e) => DeleteSelectedJob();

            Label membersLabel = new Label
            {
                Text = "Members of selected broadcast",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 230),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            membersGrid = new DataGridView
            {
                Location = new Point(10, 254),
                Size = new Size(722, 176),
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

            membersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Variable", HeaderText = "Variable", FillWeight = 75 });
            membersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", FillWeight = 40 });
            membersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "OSC Address", FillWeight = 65 });
            membersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Permanence", HeaderText = "Permanence", FillWeight = 90 });
            membersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 65 });

            membersGrid.SelectionChanged += (s, e) => UpdateMemberButtonState();

            membersGrid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            removeMemberButton = MakeButton("Remove Member", 10, 436, 140, AnchorStyles.Bottom | AnchorStyles.Left);
            changeAddressButton = MakeButton("Change OSC Address", 160, 436, 160, AnchorStyles.Bottom | AnchorStyles.Left);
            renameMemberVarButton = MakeButton("Rename Variable", 330, 436, 140, AnchorStyles.Bottom | AnchorStyles.Left);

            replaceMemberVarButton = MakeButton("Replace Target", 10, 470, 160, AnchorStyles.Bottom | AnchorStyles.Left);
            convertMemberPointerButton = MakeButton("Convert to Pointer Path", 180, 470, 182, AnchorStyles.Bottom | AnchorStyles.Left);
            makePermanentMemberButton = MakeButton("Make Permanent...", 372, 470, 170, AnchorStyles.Bottom | AnchorStyles.Left);

            removeMemberButton.Click += (s, e) => RemoveSelectedMember();
            changeAddressButton.Click += (s, e) => ChangeSelectedMemberAddress();
            renameMemberVarButton.Click += (s, e) => RenameVariable(GetSelectedMember()?.Variable);
            replaceMemberVarButton.Click += (s, e) => RequestReplaceTarget(GetSelectedMember()?.Variable);
            convertMemberPointerButton.Click += (s, e) => ConvertToPointerPath(GetSelectedMember()?.Variable);
            makePermanentMemberButton.Click += (s, e) => MakePermanent(GetSelectedMember()?.Variable);

            broadcastsTab.Controls.Add(jobsLabel);
            broadcastsTab.Controls.Add(jobsGrid);
            broadcastsTab.Controls.Add(startStopButton);
            broadcastsTab.Controls.Add(renameJobButton);
            broadcastsTab.Controls.Add(deleteJobButton);
            broadcastsTab.Controls.Add(membersLabel);
            broadcastsTab.Controls.Add(membersGrid);
            broadcastsTab.Controls.Add(removeMemberButton);
            broadcastsTab.Controls.Add(changeAddressButton);
            broadcastsTab.Controls.Add(renameMemberVarButton);
            broadcastsTab.Controls.Add(replaceMemberVarButton);
            broadcastsTab.Controls.Add(convertMemberPointerButton);
            broadcastsTab.Controls.Add(makePermanentMemberButton);

            tabs.TabPages.Add(variablesTab);
            tabs.TabPages.Add(broadcastsTab);

            tabFrame.Controls.Add(tabs);

            Button backButton = new Button
            {
                Text = "< Back to Welcome",
                Location = new Point(24, 640),
                Size = new Size(150, 32),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            backButton.Click += (s, e) => BackToWelcomeRequested?.Invoke();

            reconnectLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9),
                ForeColor = SystemColors.GrayText,
                Location = new Point(190, 648),
                Size = new Size(586, 20),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            Controls.Add(title);
            Controls.Add(reconnectLabel);
            Controls.Add(newScanButton);
            Controls.Add(saveSessionButton);
            Controls.Add(tabFrame);
            Controls.Add(backButton);

            refreshTimer = new System.Windows.Forms.Timer { Interval = 300 };
            refreshTimer.Tick += (s, e) => RefreshLiveValues();
            refreshTimer.Start();

            RefreshVariablesGrid();
            RefreshJobsGrid();
            UpdateMemberButtonState();

            CyberTheme.Apply(this);

            // CyberTheme.Apply's generic pass recolors every plain Panel
            // to the page background, which would erase tabFrame's
            // deliberately different (lighter) frame color - reassert it
            // last so it wins.
            tabFrame.BackColor = CyberTheme.FrameDark;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Button MakeButton(string text, int x, int y, int width, AnchorStyles? anchor = null)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 30),
                Font = new Font("Segoe UI", 9),
                Anchor = anchor ?? (AnchorStyles.Top | AnchorStyles.Left)
            };
        }

        // ------------------------------------------------------------
        // Grid population and live refresh
        // ------------------------------------------------------------

        private void RefreshVariablesGrid()
        {
            variablesGrid.Rows.Clear();

            foreach (MemoryVariable variable in savedVariables)
            {
                object? value = variable.Scanner?.ReadValue(variable);

                int rowIndex = variablesGrid.Rows.Add(
                    variable.Name,
                    variable.Type.ToString(),
                    variable.SourceProcessName,
                    "",
                    FormatValue(variable, value));

                variablesGrid.Rows[rowIndex].Tag = variable;
                ApplyPermanenceCell(variablesGrid.Rows[rowIndex].Cells["Permanence"], variable);
            }

            UpdateVariableButtonState();
        }

        private void UpdateVariableButtonState()
        {
            bool hasVariable = GetSelectedVariable() != null;

            CyberTheme.SetButtonActive(sendIndividuallyButton, hasVariable);
            CyberTheme.SetButtonActive(addToBroadcastButton, hasVariable);
            CyberTheme.SetButtonActive(createBroadcastButton, hasVariable);
            CyberTheme.SetButtonActive(convertVarPointerButton, hasVariable);
            CyberTheme.SetButtonActive(renameVarButton, hasVariable);
            CyberTheme.SetButtonActive(replaceVarButton, hasVariable);
            CyberTheme.SetButtonActive(removeVarButton, hasVariable);
            CyberTheme.SetButtonActive(makePermanentVarButton, hasVariable);
        }

        private void RefreshJobsGrid()
        {
            OscJob? previouslySelected = GetSelectedJob();

            jobsGrid.Rows.Clear();

            foreach (OscJob job in broadcastManager.Jobs)
            {
                int rowIndex = jobsGrid.Rows.Add(
                    job.Name,
                    job.IsGroup ? "Group" : "Variable",
                    $"{job.IpAddress}:{job.Port}",
                    job.IsRunning ? "sending" : "stopped",
                    job.Items.Count.ToString());

                jobsGrid.Rows[rowIndex].Tag = job;

                if (job == previouslySelected)
                {
                    jobsGrid.Rows[rowIndex].Selected = true;
                }
            }

            RefreshMembersGrid();
        }

        private void RefreshMembersGrid()
        {
            membersGrid.Rows.Clear();

            OscJob? job = GetSelectedJob();

            if (job != null)
            {
                foreach (OscBroadcastItem item in job.Items)
                {
                    object? value = item.Variable.Scanner?.ReadValue(item.Variable);

                    int rowIndex = membersGrid.Rows.Add(
                        item.Variable.Name,
                        item.Variable.Type.ToString(),
                        item.OscAddress,
                        "",
                        FormatValue(item.Variable, value));

                    membersGrid.Rows[rowIndex].Tag = item;
                    ApplyPermanenceCell(membersGrid.Rows[rowIndex].Cells["Permanence"], item.Variable);
                }
            }

            UpdateMemberButtonState();
        }

        private void RefreshLiveValues()
        {
            foreach (DataGridViewRow row in variablesGrid.Rows)
            {
                if (row.Tag is MemoryVariable variable)
                {
                    object? value = variable.Scanner?.ReadValue(variable);
                    row.Cells["Value"].Value = FormatValue(variable, value);
                    ApplyPermanenceCell(row.Cells["Permanence"], variable);
                }
            }

            bool anyRunning = false;

            foreach (DataGridViewRow row in jobsGrid.Rows)
            {
                if (row.Tag is OscJob job)
                {
                    row.Cells["Status"].Value = job.IsRunning ? "sending" : "stopped";
                    anyRunning = anyRunning || job.IsRunning;
                }
            }

            foreach (DataGridViewRow row in membersGrid.Rows)
            {
                if (row.Tag is OscBroadcastItem item)
                {
                    object? value = item.Variable.Scanner?.ReadValue(item.Variable);
                    row.Cells["Value"].Value = FormatValue(item.Variable, value);
                    ApplyPermanenceCell(row.Cells["Permanence"], item.Variable);
                }
            }

            reconnectLabel.Text = ReconnectStatus.Message.Length > 0
                ? $"{ReconnectStatus.When:HH:mm:ss}  {ReconnectStatus.Message}"
                : "";
        }

        private static void ApplyPermanenceCell(DataGridViewCell cell, MemoryVariable variable)
        {
            cell.Value = PermanenceWorkflow.DescribeStatus(variable);

            Color color = PermanenceColor(variable);
            cell.Style.ForeColor = color;
            cell.Style.SelectionForeColor = color;
        }

        private static Color PermanenceColor(MemoryVariable variable)
        {
            if (variable.PermanenceState == PermanenceState.None)
            {
                return variable.PointerPath != null ? CyberTheme.Text : CyberTheme.Muted;
            }

            MemoryScanner? scanner = variable.Scanner;

            if (scanner == null || scanner.HasExited || variable.PermanentPaths.Count == 0)
            {
                return CyberTheme.Warning;
            }

            if (variable.PermanenceState == PermanenceState.Provisional)
            {
                return variable.LastConsensus.Health == PermanenceHealth.Lost
                    ? CyberTheme.Danger
                    : CyberTheme.Warning;
            }

            return variable.LastConsensus.Health switch
            {
                PermanenceHealth.Good => CyberTheme.Accent,
                PermanenceHealth.Degraded => CyberTheme.Warning,
                PermanenceHealth.Lost => CyberTheme.Danger,
                _ => CyberTheme.Text
            };
        }

        private static string FormatValue(MemoryVariable variable, object? value)
        {
            if (value == null)
            {
                return "<read failed>";
            }

            return variable.Type == DataType.String
                ? DisplayText.Sanitize(value.ToString())
                : value.ToString() ?? "<read failed>";
        }

        private void UpdateMemberButtonState()
        {
            bool hasJob = GetSelectedJob() != null;
            bool hasMember = GetSelectedMember() != null;

            CyberTheme.SetButtonActive(startStopButton, hasJob);
            CyberTheme.SetButtonActive(renameJobButton, hasJob);
            CyberTheme.SetButtonActive(deleteJobButton, hasJob);

            CyberTheme.SetButtonActive(removeMemberButton, hasMember);
            CyberTheme.SetButtonActive(changeAddressButton, hasMember);
            CyberTheme.SetButtonActive(renameMemberVarButton, hasMember);
            CyberTheme.SetButtonActive(replaceMemberVarButton, hasMember);
            CyberTheme.SetButtonActive(convertMemberPointerButton, hasMember);
            CyberTheme.SetButtonActive(makePermanentMemberButton, hasMember);
        }

        private void RequestReplaceTarget(MemoryVariable? variable)
        {
            if (variable == null)
            {
                return;
            }

            if (variable.Scanner == null)
            {
                MessageBox.Show(
                    FindForm(),
                    "This variable has no active connection to its process. Start a new scan " +
                    "session against that process first.",
                    "Can't Replace Target",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            ReplaceTargetRequested?.Invoke(variable);
        }

        private MemoryVariable? GetSelectedVariable()
        {
            return variablesGrid.SelectedRows.Count > 0
                ? variablesGrid.SelectedRows[0].Tag as MemoryVariable
                : null;
        }

        private OscJob? GetSelectedJob()
        {
            return jobsGrid.SelectedRows.Count > 0
                ? jobsGrid.SelectedRows[0].Tag as OscJob
                : null;
        }

        private OscBroadcastItem? GetSelectedMember()
        {
            return membersGrid.SelectedRows.Count > 0
                ? membersGrid.SelectedRows[0].Tag as OscBroadcastItem
                : null;
        }

        // ------------------------------------------------------------
        // Saved variable actions
        // ------------------------------------------------------------

        private void SendSelectedIndividually()
        {
            MemoryVariable? variable = GetSelectedVariable();

            if (variable == null)
            {
                return;
            }

            string? oscAddress = PromptOscAddress(variable.Name);

            if (oscAddress == null)
            {
                return;
            }

            (string Ip, int Port)? destination = PromptDestination();

            if (destination == null)
            {
                return;
            }

            string? jobName = ResolveJobNameConflict(variable.Name, null);

            if (jobName == null)
            {
                return;
            }

            OscJob job = new OscJob
            {
                Name = jobName,
                IpAddress = destination.Value.Ip,
                Port = destination.Value.Port,
                IsGroup = false
            };

            job.Items.Add(new OscBroadcastItem(variable, oscAddress));
            broadcastManager.Jobs.Add(job);
            savedVariables.Remove(variable);

            try
            {
                job.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    FindForm(),
                    $"Could not start sending: {ex.Message}\n\nIt's been saved under Broadcasts - fix the destination and start it from there.",
                    "Could Not Start",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            RefreshVariablesGrid();
            RefreshJobsGrid();
        }

        private void AddSelectedToExistingBroadcast()
        {
            MemoryVariable? variable = GetSelectedVariable();

            if (variable == null)
            {
                return;
            }

            if (broadcastManager.Jobs.Count == 0)
            {
                MessageBox.Show(
                    FindForm(),
                    "There are no existing broadcasts yet. Try Create New Broadcast instead.",
                    "No Broadcasts",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            using ChoiceDialog choiceDialog = new ChoiceDialog(
                "Add to Broadcast",
                "Choose a broadcast to add this variable to:",
                broadcastManager.Jobs.Select(j =>
                    $"{j.Name} -> {j.IpAddress}:{j.Port} ({(j.IsRunning ? "sending" : "stopped")})"));

            if (choiceDialog.ShowDialog(FindForm()) != DialogResult.OK || choiceDialog.SelectedIndex < 0)
            {
                return;
            }

            OscJob job = broadcastManager.Jobs[choiceDialog.SelectedIndex];

            string? oscAddress = PromptOscAddress(variable.Name);

            if (oscAddress == null)
            {
                return;
            }

            job.Items.Add(new OscBroadcastItem(variable, oscAddress));
            savedVariables.Remove(variable);

            RefreshVariablesGrid();
            RefreshJobsGrid();

            MessageBox.Show(
                FindForm(),
                $"Added \"{variable.Name}\" to \"{job.Name}\".",
                "Added",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void CreateBroadcastWithSelected()
        {
            MemoryVariable? variable = GetSelectedVariable();

            if (variable == null)
            {
                return;
            }

            using TextPromptDialog nameDialog = new TextPromptDialog(
                "New Broadcast",
                "Name for this new OSC broadcast (group):");

            if (nameDialog.ShowDialog(FindForm()) != DialogResult.OK || string.IsNullOrWhiteSpace(nameDialog.Value))
            {
                return;
            }

            string? jobName = ResolveJobNameConflict(nameDialog.Value, null);

            if (jobName == null)
            {
                return;
            }

            (string Ip, int Port)? destination = PromptDestination();

            if (destination == null)
            {
                return;
            }

            string? oscAddress = PromptOscAddress(variable.Name);

            if (oscAddress == null)
            {
                return;
            }

            OscJob job = new OscJob
            {
                Name = jobName,
                IpAddress = destination.Value.Ip,
                Port = destination.Value.Port,
                IsGroup = true
            };

            job.Items.Add(new OscBroadcastItem(variable, oscAddress));
            broadcastManager.Jobs.Add(job);
            savedVariables.Remove(variable);

            try
            {
                job.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    FindForm(),
                    $"Created \"{job.Name}\" but could not start sending: {ex.Message}",
                    "Could Not Start",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            RefreshVariablesGrid();
            RefreshJobsGrid();
        }

        private void RemoveSelectedVariable()
        {
            MemoryVariable? variable = GetSelectedVariable();

            if (variable == null)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                FindForm(),
                $"Remove \"{variable.Name}\"?",
                "Confirm Remove",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                savedVariables.Remove(variable);
                RefreshVariablesGrid();
            }
        }

        // ------------------------------------------------------------
        // Broadcast actions
        // ------------------------------------------------------------

        private void ToggleSelectedJob()
        {
            OscJob? job = GetSelectedJob();

            if (job == null)
            {
                return;
            }

            if (job.IsRunning)
            {
                job.Stop();
            }
            else
            {
                try
                {
                    job.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        FindForm(),
                        $"Could not start: {ex.Message}",
                        "Could Not Start",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            RefreshJobsGrid();
        }

        private void RenameSelectedJob()
        {
            OscJob? job = GetSelectedJob();

            if (job == null)
            {
                return;
            }

            using TextPromptDialog dialog = new TextPromptDialog("Rename Broadcast", "New name:", job.Name);

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value))
            {
                return;
            }

            string? resolved = ResolveJobNameConflict(dialog.Value, job);

            if (resolved == null)
            {
                return;
            }

            job.Name = resolved;
            RefreshJobsGrid();
        }

        private void DeleteSelectedJob()
        {
            OscJob? job = GetSelectedJob();

            if (job == null)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                FindForm(),
                $"Delete broadcast \"{job.Name}\"? Its member variables will be lost unless you note them down first.",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                job.Stop();
                broadcastManager.Jobs.Remove(job);
                RefreshJobsGrid();
            }
        }

        private void RemoveSelectedMember()
        {
            OscJob? job = GetSelectedJob();
            OscBroadcastItem? item = GetSelectedMember();

            if (job == null || item == null)
            {
                return;
            }

            job.Items.Remove(item);
            RefreshMembersGrid();
            RefreshJobsGrid();
        }

        private void ChangeSelectedMemberAddress()
        {
            OscBroadcastItem? item = GetSelectedMember();

            if (item == null)
            {
                return;
            }

            using TextPromptDialog dialog = new TextPromptDialog(
                "Change OSC Address",
                "New OSC address:",
                item.OscAddress);

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value))
            {
                return;
            }

            item.OscAddress = NormalizeOscAddress(dialog.Value);
            RefreshMembersGrid();
        }

        // ------------------------------------------------------------
        // Shared variable actions (used by both tabs)
        // ------------------------------------------------------------

        private void RenameVariable(MemoryVariable? variable)
        {
            if (variable == null)
            {
                return;
            }

            using TextPromptDialog dialog = new TextPromptDialog("Rename Variable", "New name:", variable.Name);

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value))
            {
                return;
            }

            string? resolved = ResolveVariableNameConflict(dialog.Value, variable);

            if (resolved == null)
            {
                return;
            }

            variable.Name = resolved;
            RefreshVariablesGrid();
            RefreshMembersGrid();
        }

        private IEnumerable<MemoryVariable> AllVariables()
        {
            return savedVariables
                .Concat(broadcastManager.Jobs.SelectMany(j => j.Items.Select(i => i.Variable)))
                .Distinct();
        }

        /// <summary>
        /// Opens the Make Permanent wizard for the selected variable, along
        /// with every other variable from the same program so one scan
        /// serves them all.
        /// </summary>
        private void MakePermanent(MemoryVariable? variable)
        {
            if (variable == null)
            {
                return;
            }

            bool hasStoredScan =
                variable.PermanenceState == PermanenceState.Learned ||
                variable.PermanenceState == PermanenceState.Provisional;

            if (!hasStoredScan && (variable.Scanner == null || variable.Scanner.HasExited))
            {
                MessageBox.Show(
                    FindForm(),
                    $"{(string.IsNullOrWhiteSpace(variable.SourceProcessName) ? "The program" : variable.SourceProcessName)} " +
                    "isn't running. Start it, get the value showing on screen, and try again.",
                    "Can't Make Permanent",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            List<MemoryVariable> group = AllVariables()
                .Where(v => string.Equals(v.SourceProcessName, variable.SourceProcessName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            using MakePermanentDialog dialog = new MakePermanentDialog(group, variable, SaveSession);
            dialog.ShowDialog(FindForm());

            if (dialog.Changed)
            {
                RefreshVariablesGrid();
                RefreshMembersGrid();
            }
        }

        /// <summary>
        /// One-click auto-detect only (module+offset for a static/global
        /// variable). The console's manual pointer-chain builder (for
        /// heap/dynamic addresses) isn't ported to the GUI yet.
        /// </summary>
        private void ConvertToPointerPath(MemoryVariable? variable)
        {
            if (variable == null)
            {
                return;
            }

            if (variable.PermanenceState != PermanenceState.None)
            {
                MessageBox.Show(
                    FindForm(),
                    $"\"{variable.Name}\" already uses Make Permanent chains, which take priority over a " +
                    "manual pointer path - there's nothing to convert.",
                    "Already Permanent",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            if (variable.Scanner == null)
            {
                MessageBox.Show(
                    FindForm(),
                    "This variable has no active connection to its process right now.",
                    "Can't Convert",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            ProcessModule? module = variable.Scanner.FindModuleContaining(variable.Address);

            if (module != null)
            {
                long offset = variable.Address.ToInt64() - module.BaseAddress.ToInt64();

                DialogResult choice = MessageBox.Show(
                    FindForm(),
                    $"This address is inside \"{module.ModuleName}\" at offset 0x{offset:X}.\n\n" +
                    "That looks like a static/global variable - a direct module+offset pointer path " +
                    "should survive the target restarting.\n\n" +
                    "Yes - Use this (recommended)\nNo - Build a manual pointer chain instead\n" +
                    "Cancel - Cancel",
                    "Convert to Pointer Path",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (choice == DialogResult.Yes)
                {
                    variable.PointerPath = new PointerPath
                    {
                        ModuleName = module.ModuleName,
                        BaseOffset = offset,
                        IsPointerChain = false
                    };

                    RefreshVariablesGrid();
                    RefreshMembersGrid();

                    MessageBox.Show(
                        FindForm(),
                        $"Converted. \"{variable.Name}\" will now be re-found via " +
                        $"{module.ModuleName}+0x{offset:X} even after a restart.",
                        "Converted",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return;
                }

                if (choice != DialogResult.No)
                {
                    return;
                }

                // Fall through to the manual chain builder.
            }
            else
            {
                DialogResult choice = MessageBox.Show(
                    FindForm(),
                    $"0x{variable.Address.ToInt64():X} isn't inside any loaded module - it's likely a " +
                    "dynamic/heap allocation. A simple module+offset won't reliably survive a restart.\n\n" +
                    "Build a manual pointer chain instead? Useful if you've already found one with " +
                    "another tool's pointer scanner.",
                    "Build Manual Pointer Chain?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (choice != DialogResult.Yes)
                {
                    return;
                }
            }

            using PointerChainDialog chainDialog = new PointerChainDialog(
                variable.Scanner,
                variable.Type,
                variable.StringEncoding);

            if (chainDialog.ShowDialog(FindForm()) == DialogResult.OK && chainDialog.Result != null)
            {
                variable.PointerPath = chainDialog.Result;

                RefreshVariablesGrid();
                RefreshMembersGrid();

                MessageBox.Show(
                    FindForm(),
                    $"Converted \"{variable.Name}\" to a manual pointer chain.",
                    "Converted",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        // ------------------------------------------------------------
        // Name-conflict resolution delegates to the shared resolver so
        // ScanView's save path and this view catch duplicates identically.
        // ------------------------------------------------------------

        private string? ResolveVariableNameConflict(string candidateName, MemoryVariable? exclude) =>
            NameConflictResolver.ResolveVariableName(FindForm(), savedVariables, broadcastManager, candidateName, exclude);

        private string? ResolveJobNameConflict(string candidateName, OscJob? exclude) =>
            NameConflictResolver.ResolveJobName(FindForm(), broadcastManager, candidateName, exclude);

        // ------------------------------------------------------------
        // Small shared prompts
        // ------------------------------------------------------------

        private string? PromptOscAddress(string suggestedName)
        {
            using TextPromptDialog dialog = new TextPromptDialog(
                "OSC Address",
                "OSC address (e.g. /osc/myvar):",
                $"/{suggestedName}");

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value))
            {
                return null;
            }

            return NormalizeOscAddress(dialog.Value);
        }

        private static string NormalizeOscAddress(string address)
        {
            string trimmed = address.Trim();
            return trimmed.StartsWith("/") ? trimmed : "/" + trimmed;
        }

        private (string Ip, int Port)? PromptDestination()
        {
            using DestinationDialog dialog = new DestinationDialog();

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(dialog.Ip))
            {
                MessageBox.Show(FindForm(), "IP address cannot be empty.", "Invalid Destination",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            if (dialog.Port < 1 || dialog.Port > 65535)
            {
                MessageBox.Show(FindForm(), "Port must be a number from 1 to 65535.", "Invalid Destination",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            return (dialog.Ip, dialog.Port);
        }

        private void SaveSession(IWin32Window? owner = null)
        {
            if (savedVariables.Count == 0 && broadcastManager.Jobs.Count == 0)
            {
                MessageBox.Show(
                    owner ?? FindForm(),
                    "Nothing to save yet.",
                    "Save Session",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save VariABLE OSC Session",
                Filter = "VariABLE OSC session (*.json)|*.json|All files (*.*)|*.*",
                FileName = "session.json"
            };

            if (dialog.ShowDialog(owner ?? FindForm()) != DialogResult.OK)
            {
                return;
            }

            try
            {
                SessionData data = SessionStore.BuildSessionData(savedVariables, broadcastManager.Jobs);
                SessionStore.Save(dialog.FileName, data);
                RecentSessionsStore.AddRecent(dialog.FileName);

                MessageBox.Show(
                    owner ?? FindForm(),
                    $"Saved {data.SavedVariables.Count} variable(s) and {data.Jobs.Count} broadcast(s) to:\n{dialog.FileName}",
                    "Session Saved",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    owner ?? FindForm(),
                    $"Could not save session:\n{ex.Message}",
                    "Save Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
