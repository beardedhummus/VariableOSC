using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// The single persistent top-level window for the step-by-step scan
    /// flow (Welcome -> Process Picker -> Scan -> Saved Variables /
    /// Broadcasts). Each step is a UserControl swapped into contentPanel,
    /// so moving between steps never opens a second window - only
    /// auxiliary windows (e.g. a future Settings dialog) get their own
    /// Form.
    /// </summary>
    public class MainForm : Form
    {
        private readonly Panel contentPanel;

        private Control? currentView;

        public MainForm()
        {
            // Every control in this app is laid out with absolute pixel
            // coordinates rather than through the WinForms designer. Left
            // at its default, each nested container (Form -> Panel ->
            // UserControl) re-applies its own automatic DPI/font scaling
            // on top of the others, compounding into badly wrong positions
            // (controls pushed far off the visible window). Turning it off
            // everywhere makes 1 unit == 1 pixel, consistently.
            AutoScaleMode = AutoScaleMode.None;

            string version =
                "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

            // The version is shown right in the title bar (not just on the
            // welcome screen) specifically so a bug report/screenshot
            // always makes it unambiguous which build was actually running.
            Text = $"VariABLE OSC {version}";

            // Title bar / taskbar icon: the exe's own embedded icon
            // (ApplicationIcon in the csproj), which WinForms doesn't pick
            // up for a Form on its own.
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
            }
            catch (Exception)
            {
                // Cosmetic only - keep the default icon.
            }

            // Set ClientSize (the drawable interior) before MinimumSize:
            // MinimumSize constrains the Form's overall Size, which
            // includes the title bar and borders, so setting it first
            // would under-count the usable interior area.
            ClientSize = new Size(800, 680);
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterScreen;

            contentPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            Controls.Add(contentPanel);

            CyberTheme.Apply(this);
        }

        public void ShowView(Control view)
        {
            Control? previous = currentView;

            SuspendLayout();

            view.Dock = DockStyle.Fill;
            contentPanel.Controls.Add(view);
            view.BringToFront();

            currentView = view;

            ResumeLayout(true);

            if (previous != null)
            {
                contentPanel.Controls.Remove(previous);
                previous.Dispose();
            }
        }
    }
}
