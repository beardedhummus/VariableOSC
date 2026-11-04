using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// The app's landing screen: start a fresh scan session, jump back
    /// into a previously saved one (recent-files list or browse), or -
    /// when a session is already active in memory - continue it or
    /// deliberately discard it. Its content sits in a fixed-size "card"
    /// centered within whatever space the shell window gives it, so it
    /// still looks intentional once later screens make the window wider.
    /// Built with plain stock controls so a theme/skin library can be
    /// dropped in later without a rewrite.
    /// </summary>
    public class WelcomeView : UserControl
    {
        private static readonly string VersionString =
            "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

        private readonly Panel card;
        private readonly ListBox recentList;
        private readonly bool hasActiveSession;

        private List<string> recentPaths = new();

        /// <summary>Fires when there's no active session and the user
        /// wants to start scanning from scratch.</summary>
        public event Action? NewSessionRequested;

        /// <summary>Fires when there IS an active session and the user
        /// wants to go straight back to it (no data changes).</summary>
        public event Action? ContinueSessionRequested;

        /// <summary>Fires after the user confirms discarding the active
        /// session to start a new one. The caller is responsible for
        /// actually clearing state before navigating onward.</summary>
        public event Action? StartOverRequested;

        /// <summary>Fires with the file to load, only after any needed
        /// "discard current session?" confirmation already happened here.
        /// The caller is responsible for clearing state before loading.</summary>
        public event Action<string>? SessionLoadRequested;

        public WelcomeView(bool hasActiveSession)
        {
            this.hasActiveSession = hasActiveSession;

            AutoScaleMode = AutoScaleMode.None;

            Dock = DockStyle.Fill;

            card = new Panel
            {
                Size = new Size(480, 480),
                Anchor = AnchorStyles.None
            };

            Label title = new Label
            {
                Text = "VariABLE OSC",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 28),
                Size = new Size(card.Width, 44)
            };

            Label version = new Label
            {
                Text = VersionString,
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = Color.Gray,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 72),
                Size = new Size(card.Width, 24)
            };

            card.Controls.Add(title);
            card.Controls.Add(version);

            if (hasActiveSession)
            {
                Button continueButton = new Button
                {
                    Text = "Continue Session",
                    Size = new Size(180, 40),
                    Location = new Point(60, 130),
                    Font = new Font("Segoe UI", 10)
                };

                continueButton.Click += (s, e) => ContinueSessionRequested?.Invoke();

                Button loadButtonActive = new Button
                {
                    Text = "Load Session",
                    Size = new Size(180, 40),
                    Location = new Point(260, 130),
                    Font = new Font("Segoe UI", 10)
                };

                loadButtonActive.Click += (s, e) => BrowseForSession();

                Button startOverButton = new Button
                {
                    Text = "Start Over",
                    Size = new Size(160, 28),
                    Location = new Point(160, 178),
                    Font = new Font("Segoe UI", 9)
                };

                startOverButton.Click += (s, e) => ConfirmStartOver();

                card.Controls.Add(continueButton);
                card.Controls.Add(loadButtonActive);
                card.Controls.Add(startOverButton);
            }
            else
            {
                Button newSessionButton = new Button
                {
                    Text = "New Session",
                    Size = new Size(180, 40),
                    Location = new Point(60, 130),
                    Font = new Font("Segoe UI", 10)
                };

                newSessionButton.Click += (s, e) => NewSessionRequested?.Invoke();

                Button loadButton = new Button
                {
                    Text = "Load Session",
                    Size = new Size(180, 40),
                    Location = new Point(260, 130),
                    Font = new Font("Segoe UI", 10)
                };

                loadButton.Click += (s, e) => BrowseForSession();

                card.Controls.Add(newSessionButton);
                card.Controls.Add(loadButton);
            }

            Label recentLabel = new Label
            {
                Text = "Recent Sessions",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(60, 222),
                Size = new Size(360, 20)
            };

            recentList = new ListBox
            {
                Location = new Point(60, 246),
                Size = new Size(360, 150),
                Font = new Font("Segoe UI", 9.5f)
            };

            recentList.DoubleClick += (s, e) => LoadSelectedRecent();

            Button openRecentButton = new Button
            {
                Text = "Open Selected",
                Size = new Size(160, 32),
                Location = new Point(160, 408),
                Font = new Font("Segoe UI", 9.5f)
            };

            openRecentButton.Click += (s, e) => LoadSelectedRecent();

            card.Controls.Add(recentLabel);
            card.Controls.Add(recentList);
            card.Controls.Add(openRecentButton);

            Controls.Add(card);

            Resize += (s, e) => CenterCard();

            RefreshRecentList();

            CyberTheme.Apply(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CenterCard();
        }

        private void CenterCard()
        {
            card.Location = new Point(
                Math.Max(0, (Width - card.Width) / 2),
                Math.Max(0, (Height - card.Height) / 2));
        }

        private void RefreshRecentList()
        {
            recentList.Items.Clear();

            recentPaths = RecentSessionsStore.Load();

            if (recentPaths.Count == 0)
            {
                recentList.Items.Add("(No recent sessions yet)");
                recentList.Enabled = false;
                return;
            }

            recentList.Enabled = true;

            foreach (string path in recentPaths)
            {
                recentList.Items.Add(Path.GetFileName(path));
            }
        }

        private void LoadSelectedRecent()
        {
            int index = recentList.SelectedIndex;

            if (index < 0 || index >= recentPaths.Count)
            {
                return;
            }

            TryRaiseSessionLoad(recentPaths[index]);
        }

        private void BrowseForSession()
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Load VariABLE OSC Session",
                Filter = "VariABLE OSC session (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            {
                TryRaiseSessionLoad(dialog.FileName);
            }
        }

        private void TryRaiseSessionLoad(string path)
        {
            if (hasActiveSession)
            {
                DialogResult result = MessageBox.Show(
                    FindForm(),
                    "You have an unsaved session in progress. Loading a different one will " +
                    "discard it. Continue?",
                    "Discard Current Session?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                {
                    return;
                }
            }

            RecentSessionsStore.AddRecent(path);
            RefreshRecentList();
            SessionLoadRequested?.Invoke(path);
        }

        private void ConfirmStartOver()
        {
            DialogResult result = MessageBox.Show(
                FindForm(),
                "This will discard your current session's saved variables and broadcasts " +
                "(unless you've already saved it). Start over?",
                "Discard Current Session?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                StartOverRequested?.Invoke();
            }
        }
    }
}
