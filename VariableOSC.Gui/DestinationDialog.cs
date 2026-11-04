using System.Drawing;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// Prompts for an OSC destination (IP + port). Another small modal
    /// auxiliary dialog, same rationale as TextPromptDialog.
    /// </summary>
    public class DestinationDialog : Form
    {
        private readonly TextBox ipBox;
        private readonly TextBox portBox;

        public string Ip => ipBox.Text.Trim();

        public int Port => int.TryParse(portBox.Text.Trim(), out int p) ? p : 0;

        public DestinationDialog(string initialIp = "127.0.0.1", string initialPort = "9000")
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "OSC Destination";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(320, 170);

            Label ipLabel = new Label
            {
                Text = "IP Address:",
                Location = new Point(16, 18),
                Size = new Size(90, 20),
                Font = new Font("Segoe UI", 9.5f)
            };

            ipBox = new TextBox
            {
                Text = initialIp,
                Location = new Point(112, 15),
                Size = new Size(192, 24),
                Font = new Font("Segoe UI", 10)
            };

            Label portLabel = new Label
            {
                Text = "Port:",
                Location = new Point(16, 54),
                Size = new Size(90, 20),
                Font = new Font("Segoe UI", 9.5f)
            };

            portBox = new TextBox
            {
                Text = initialPort,
                Location = new Point(112, 51),
                Size = new Size(192, 24),
                Font = new Font("Segoe UI", 10)
            };

            Button okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(144, 110),
                Size = new Size(80, 32)
            };

            Button cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(224, 110),
                Size = new Size(80, 32)
            };

            Controls.Add(ipLabel);
            Controls.Add(ipBox);
            Controls.Add(portLabel);
            Controls.Add(portBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            CyberTheme.Apply(this);
        }
    }
}
