using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// Picks one item from a short list (e.g. "which broadcast?"). Another
    /// small modal auxiliary dialog, same rationale as TextPromptDialog.
    /// </summary>
    public class ChoiceDialog : Form
    {
        private readonly ListBox list;

        public int SelectedIndex => list.SelectedIndex;

        public ChoiceDialog(string title, string prompt, IEnumerable<string> options)
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 260);

            Label promptLabel = new Label
            {
                Text = prompt,
                Location = new Point(16, 16),
                Size = new Size(328, 20),
                Font = new Font("Segoe UI", 9.5f)
            };

            list = new ListBox
            {
                Location = new Point(16, 42),
                Size = new Size(328, 170),
                Font = new Font("Segoe UI", 9.5f)
            };

            list.Items.AddRange(options.Cast<object>().ToArray());

            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }

            list.DoubleClick += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            Button okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(184, 220),
                Size = new Size(80, 32)
            };

            Button cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(264, 220),
                Size = new Size(80, 32)
            };

            Controls.Add(promptLabel);
            Controls.Add(list);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            CyberTheme.Apply(this);
        }
    }
}
