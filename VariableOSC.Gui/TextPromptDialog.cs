using System.Drawing;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// A small modal "enter some text" dialog - WinForms has no built-in
    /// input box. Used for naming a saved variable now, and reusable later
    /// for OSC addresses / broadcast group names on the dashboard screen.
    /// This is exactly the kind of auxiliary window that's fine as its own
    /// Form rather than a shell view, since it's a short-lived modal
    /// prompt, not a step in the main flow.
    /// </summary>
    public class TextPromptDialog : Form
    {
        private readonly TextBox input;

        public string Value => input.Text.Trim();

        public TextPromptDialog(string title, string prompt, string initialValue = "")
        {
            AutoScaleMode = AutoScaleMode.None;

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 130);
            AcceptButton = null;

            Label promptLabel = new Label
            {
                Text = prompt,
                AutoSize = false,
                Location = new Point(16, 16),
                Size = new Size(328, 20),
                Font = new Font("Segoe UI", 9.5f)
            };

            input = new TextBox
            {
                Text = initialValue,
                Location = new Point(16, 42),
                Size = new Size(328, 24),
                Font = new Font("Segoe UI", 10)
            };

            Button okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(184, 80),
                Size = new Size(80, 32)
            };

            Button cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(264, 80),
                Size = new Size(80, 32)
            };

            Controls.Add(promptLabel);
            Controls.Add(input);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            CyberTheme.Apply(this);
        }

        protected override void OnShown(System.EventArgs e)
        {
            base.OnShown(e);
            input.SelectAll();
            input.Focus();
        }
    }
}
