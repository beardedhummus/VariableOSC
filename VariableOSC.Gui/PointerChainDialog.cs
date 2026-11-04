using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// GUI port of the console's manual pointer-chain builder (for
    /// heap/dynamic addresses, where a simple module+offset isn't enough -
    /// see PointerPath.cs in Core). Pick a module, a base offset, and
    /// whether that location holds the value directly or a pointer that
    /// needs dereferencing through a chain of further offsets; the preview
    /// re-resolves and re-reads live on every change, not just after each
    /// "Add" click.
    /// </summary>
    public class PointerChainDialog : Form
    {
        private readonly MemoryScanner scanner;
        private readonly DataType previewType;
        private readonly StringEncodingType previewEncoding;

        private readonly ComboBox moduleCombo;
        private readonly TextBox baseOffsetBox;
        private readonly RadioButton directRadio;
        private readonly RadioButton chainRadio;
        private readonly ListBox offsetsList;
        private readonly Button removeLastButton;
        private readonly TextBox nextOffsetBox;
        private readonly Button addOffsetButton;
        private readonly Label previewLabel;
        private readonly Button okButton;

        private readonly List<long> offsets = new();

        public PointerPath? Result { get; private set; }

        public PointerChainDialog(
            MemoryScanner scanner,
            DataType previewType,
            StringEncodingType previewEncoding = StringEncodingType.Ascii)
        {
            this.scanner = scanner;
            this.previewType = previewType;
            this.previewEncoding = previewEncoding;

            AutoScaleMode = AutoScaleMode.None;
            Text = "Build a Pointer Chain";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 430);

            Label moduleLabel = new Label
            {
                Text = "Module:",
                Location = new Point(16, 16),
                Size = new Size(80, 22),
                Font = new Font("Segoe UI", 9.5f)
            };

            moduleCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(100, 14),
                Size = new Size(340, 24),
                Font = new Font("Segoe UI", 9.5f)
            };

            moduleCombo.Items.AddRange(scanner.ListModuleNames().Cast<object>().ToArray());

            if (moduleCombo.Items.Count > 0)
            {
                moduleCombo.SelectedIndex = 0;
            }

            Label offsetLabel = new Label
            {
                Text = "Base offset:",
                Location = new Point(16, 50),
                Size = new Size(80, 22),
                Font = new Font("Segoe UI", 9.5f)
            };

            baseOffsetBox = new TextBox
            {
                Location = new Point(100, 48),
                Size = new Size(160, 24),
                Font = new Font("Segoe UI", 9.5f),
                Text = "0x0"
            };

            directRadio = new RadioButton
            {
                Text = "The value is here directly",
                Location = new Point(16, 84),
                Size = new Size(420, 22),
                Checked = true,
                Font = new Font("Segoe UI", 9.5f)
            };

            chainRadio = new RadioButton
            {
                Text = "This holds a POINTER that needs dereferencing (manual chain)",
                Location = new Point(16, 108),
                Size = new Size(420, 22),
                Font = new Font("Segoe UI", 9.5f)
            };

            Label offsetsLabel = new Label
            {
                Text = "Chain (each entry is dereferenced; the last is the final offset):",
                Location = new Point(16, 138),
                Size = new Size(420, 18),
                Font = new Font("Segoe UI", 8.5f)
            };

            offsetsList = new ListBox
            {
                Location = new Point(16, 158),
                Size = new Size(300, 90),
                Font = new Font("Segoe UI", 9.5f),
                Enabled = false
            };

            removeLastButton = new Button
            {
                Text = "Remove Last",
                Location = new Point(324, 158),
                Size = new Size(116, 28),
                Font = new Font("Segoe UI", 9),
                Enabled = false
            };

            Label nextOffsetLabel = new Label
            {
                Text = "Next offset:",
                Location = new Point(16, 258),
                Size = new Size(80, 22),
                Font = new Font("Segoe UI", 9.5f)
            };

            nextOffsetBox = new TextBox
            {
                Location = new Point(100, 256),
                Size = new Size(120, 24),
                Font = new Font("Segoe UI", 9.5f),
                Text = "0x0",
                Enabled = false
            };

            addOffsetButton = new Button
            {
                Text = "Add Offset",
                Location = new Point(228, 255),
                Size = new Size(100, 26),
                Font = new Font("Segoe UI", 9),
                Enabled = false
            };

            previewLabel = new Label
            {
                Text = "Preview: -",
                Location = new Point(16, 292),
                Size = new Size(428, 44),
                Font = new Font("Segoe UI", 9)
            };

            okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(268, 356),
                Size = new Size(80, 32),
                Enabled = false
            };

            Button cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(356, 356),
                Size = new Size(80, 32)
            };

            moduleCombo.SelectedIndexChanged += (s, e) => UpdatePreview();
            baseOffsetBox.TextChanged += (s, e) => UpdatePreview();

            directRadio.CheckedChanged += (s, e) =>
            {
                bool chainMode = chainRadio.Checked;
                offsetsList.Enabled = chainMode;
                removeLastButton.Enabled = chainMode;
                nextOffsetBox.Enabled = chainMode;
                addOffsetButton.Enabled = chainMode;
                UpdatePreview();
            };

            addOffsetButton.Click += (s, e) =>
            {
                if (TryParseOffset(nextOffsetBox.Text, out long value))
                {
                    offsets.Add(value);
                    offsetsList.Items.Add($"{offsets.Count}. 0x{value:X}");
                    nextOffsetBox.Text = "0x0";
                    UpdatePreview();
                }
                else
                {
                    MessageBox.Show(
                        this,
                        "Enter a valid offset, e.g. 0x18 or -0x4.",
                        "Invalid Offset",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            };

            removeLastButton.Click += (s, e) =>
            {
                if (offsets.Count > 0)
                {
                    offsets.RemoveAt(offsets.Count - 1);
                    offsetsList.Items.RemoveAt(offsetsList.Items.Count - 1);
                    UpdatePreview();
                }
            };

            okButton.Click += (s, e) => Result = BuildPointerPath();

            Controls.Add(moduleLabel);
            Controls.Add(moduleCombo);
            Controls.Add(offsetLabel);
            Controls.Add(baseOffsetBox);
            Controls.Add(directRadio);
            Controls.Add(chainRadio);
            Controls.Add(offsetsLabel);
            Controls.Add(offsetsList);
            Controls.Add(removeLastButton);
            Controls.Add(nextOffsetLabel);
            Controls.Add(nextOffsetBox);
            Controls.Add(addOffsetButton);
            Controls.Add(previewLabel);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            CancelButton = cancelButton;

            UpdatePreview();

            CyberTheme.Apply(this);
        }

        private void UpdatePreview()
        {
            if (moduleCombo.SelectedItem is not string moduleName ||
                !TryParseOffset(baseOffsetBox.Text, out long baseOffset))
            {
                previewLabel.Text = "Preview: pick a module and enter a valid base offset.";
                okButton.Enabled = false;
                return;
            }

            bool isChain = chainRadio.Checked;

            if (isChain && offsets.Count == 0)
            {
                previewLabel.Text = "Preview: add at least one offset for a pointer chain.";
                okButton.Enabled = false;
                return;
            }

            PointerPath candidate = new PointerPath
            {
                ModuleName = moduleName,
                BaseOffset = baseOffset,
                IsPointerChain = isChain,
                Offsets = new List<long>(offsets)
            };

            IntPtr? resolved = scanner.ResolvePointerPath(candidate);

            if (resolved == null)
            {
                previewLabel.Text = "Preview: this chain doesn't resolve (a pointer along the way is invalid).";
                okButton.Enabled = false;
                return;
            }

            object? value = previewType == DataType.String
                ? scanner.ReadString(resolved.Value, previewEncoding, 32)
                : scanner.ReadValue(resolved.Value, previewType);

            string displayValue = value == null
                ? "<read failed>"
                : previewType == DataType.String
                    ? DisplayText.Sanitize(value.ToString())
                    : value.ToString() ?? "<read failed>";

            previewLabel.Text =
                $"Preview: resolves to 0x{resolved.Value.ToInt64():X}\ncurrent value: {displayValue}";

            okButton.Enabled = true;
        }

        private PointerPath BuildPointerPath()
        {
            TryParseOffset(baseOffsetBox.Text, out long baseOffset);

            return new PointerPath
            {
                ModuleName = (string)moduleCombo.SelectedItem!,
                BaseOffset = baseOffset,
                IsPointerChain = chainRadio.Checked,
                Offsets = new List<long>(offsets)
            };
        }

        private static bool TryParseOffset(string? input, out long value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string text = input.Trim();
            bool negative = text.StartsWith("-");

            if (negative)
            {
                text = text.Substring(1);
            }

            bool isHex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

            if (isHex)
            {
                text = text.Substring(2);
            }

            bool ok = isHex
                ? long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed)
                : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

            if (!ok)
            {
                return false;
            }

            value = negative ? -parsed : parsed;
            return true;
        }
    }
}
