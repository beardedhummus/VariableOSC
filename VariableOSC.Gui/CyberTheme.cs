using System.Drawing;
using System.Windows.Forms;

namespace VariableOSC.Gui
{
    /// <summary>
    /// Dark, monospaced, neon-on-black - the "Cyber" skin the user picked
    /// from the four-option preview. Implemented by restyling the stock
    /// WinForms controls already in use (color/font/border properties)
    /// rather than pulling in a third-party control library: a new native
    /// dependency's rendering can't be verified from this Mac before it
    /// reaches the user's Windows machine, whereas these are well-
    /// understood WinForms properties that are guaranteed to compile and
    /// very likely to render close to the approved preview.
    ///
    /// Call Apply(this) as the LAST line of every Form/UserControl
    /// constructor, after every child control has been added.
    /// </summary>
    public static class CyberTheme
    {
        public static readonly Color Page = ColorTranslator.FromHtml("#0a0d0c");
        public static readonly Color Surface = ColorTranslator.FromHtml("#101513");
        public static readonly Color SurfaceAlt = ColorTranslator.FromHtml("#0d1817");
        public static readonly Color Text = ColorTranslator.FromHtml("#d3fbe9");
        public static readonly Color Muted = ColorTranslator.FromHtml("#5f8f80");
        public static readonly Color Accent = ColorTranslator.FromHtml("#00e5a8");
        public static readonly Color Border = ColorTranslator.FromHtml("#215a4a");
        public static readonly Color RowAlt = ColorTranslator.FromHtml("#0f1614");
        public static readonly Color RowSelected = ColorTranslator.FromHtml("#12332a");

        /// <summary>Neutral dark grey for disabled control text - distinct
        /// from Muted (a dark teal) since "disabled" isn't a themed state,
        /// just an unclickable one, and should read as such.</summary>
        public static readonly Color DisabledText = ColorTranslator.FromHtml("#5a5a5a");

        /// <summary>Amber for "working but not fully healthy" status text.</summary>
        public static readonly Color Warning = ColorTranslator.FromHtml("#ffb454");

        /// <summary>Soft red for "not working" status text.</summary>
        public static readonly Color Danger = ColorTranslator.FromHtml("#ff6b6b");

        /// <summary>Dark grey frame color for wrapping controls (like
        /// TabControl) whose own native border can't be recolored via
        /// simple properties.</summary>
        public static readonly Color FrameDark = ColorTranslator.FromHtml("#2a2a2a");

        private const string FontFamily = "Consolas";

        public static void Apply(Control root)
        {
            root.BackColor = Page;
            root.ForeColor = Text;
            ApplyRecursive(root);
        }

        private static void ApplyRecursive(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                RestyleFont(child);

                switch (child)
                {
                    case DataGridView grid:
                        StyleGrid(grid);
                        break;

                    case Button button:
                        StyleButton(button);
                        break;

                    case TextBox textBox:
                        StyleTextBox(textBox);
                        break;

                    case ComboBox comboBox:
                        StyleComboBox(comboBox);
                        break;

                    case NumericUpDown numeric:
                        StyleNumeric(numeric);
                        break;

                    case ListBox listBox:
                        StyleListBox(listBox);
                        break;

                    case CheckBox checkBox:
                        StyleCheckBox(checkBox);
                        break;

                    case TabControl tabControl:
                        StyleTabControl(tabControl);
                        break;

                    case Label label:
                        StyleLabel(label);
                        break;

                    // Must come before "case Panel" - TabPage is itself a
                    // Panel subclass, so without this explicit case first,
                    // recursing into a TabControl's pages would silently
                    // overwrite the Surface color StyleTabControl just set
                    // on them with the generic Panel's Page color instead.
                    case TabPage tabPage:
                        tabPage.BackColor = Surface;
                        tabPage.ForeColor = Text;
                        break;

                    case Panel panel:
                        panel.BackColor = Page;
                        break;
                }

                ApplyRecursive(child);
            }
        }

        // Every control keeps its own deliberately-chosen font SIZE and
        // weight (a 24pt bold title stays 24pt bold) - only the family
        // changes, so the size hierarchy nothing else here knows about
        // survives the reskin.
        private static void RestyleFont(Control control)
        {
            if (control.Font.FontFamily.Name == FontFamily)
            {
                return;
            }

            control.Font = new Font(FontFamily, control.Font.Size, control.Font.Style);
        }

        private static void StyleLabel(Label label)
        {
            label.BackColor = Color.Transparent;

            bool wasMuted =
                label.ForeColor == Color.Gray ||
                label.ForeColor == Color.DimGray ||
                label.ForeColor == SystemColors.GrayText;

            label.ForeColor = wasMuted ? Muted : Text;
        }

        private static void StyleButton(Button button)
        {
            button.BackColor = SurfaceAlt;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = RowSelected;
            button.FlatAppearance.MouseDownBackColor = RowSelected;
            button.Cursor = Cursors.Hand;

            // Respects whatever Enabled state the button already had (some
            // start disabled via their object initializer), then hands off
            // to SetButtonActive - see its doc comment for why.
            SetButtonActive(button, button.Enabled);
        }

        /// <summary>
        /// Use this instead of "button.Enabled = x" everywhere in the
        /// Cyber-themed GUI. WinForms' flat-button renderer ignores
        /// ForeColor entirely once Enabled is false - it always paints a
        /// fixed system disabled color regardless of theme, which read as
        /// plain black against this dark skin. Since every button's click
        /// handler in this app already no-ops safely when nothing relevant
        /// is selected, staying genuinely Enabled and only dimming the
        /// text is both safe and fully themeable.
        /// </summary>
        public static void SetButtonActive(Button button, bool active)
        {
            button.Enabled = true;
            button.ForeColor = active ? Text : DisabledText;
        }

        private static void StyleTextBox(TextBox textBox)
        {
            textBox.BackColor = Surface;
            textBox.ForeColor = Text;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleComboBox(ComboBox comboBox)
        {
            comboBox.BackColor = Surface;
            comboBox.ForeColor = Text;
            comboBox.FlatStyle = FlatStyle.Flat;
        }

        private static void StyleNumeric(NumericUpDown numeric)
        {
            numeric.BackColor = Surface;
            numeric.ForeColor = Text;
            numeric.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleListBox(ListBox listBox)
        {
            listBox.BackColor = Surface;
            listBox.ForeColor = Text;
            listBox.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void StyleCheckBox(CheckBox checkBox)
        {
            checkBox.BackColor = Color.Transparent;
            checkBox.ForeColor = Text;
        }

        private static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Surface;
            grid.GridColor = Border;
            grid.BorderStyle = BorderStyle.FixedSingle;

            // Windows visual styles otherwise override header colors
            // outright - this is required, not optional, for the header
            // styling below to have any effect.
            grid.EnableHeadersVisualStyles = false;

            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Accent;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Accent;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(FontFamily, 9f, FontStyle.Bold);

            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = RowSelected;
            grid.DefaultCellStyle.SelectionForeColor = Accent;
            grid.DefaultCellStyle.Font = new Font(FontFamily, 9f);

            grid.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Text;

            grid.RowHeadersDefaultCellStyle.BackColor = Surface;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        }

        private static void StyleTabControl(TabControl tabControl)
        {
            tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabControl.Padding = new Point(14, 6);
            tabControl.DrawItem += TabControlDrawItem;

            foreach (TabPage page in tabControl.TabPages)
            {
                page.BackColor = Surface;
                page.ForeColor = Text;
            }
        }

        // TabControl mostly ignores BackColor/ForeColor on its header
        // strip unless owner-drawn - without this the tab headers would
        // stay default Windows gray, the single most visible unstyled
        // element on an otherwise fully dark screen.
        private static void TabControlDrawItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabControl)
            {
                return;
            }

            TabPage page = tabControl.TabPages[e.Index];
            bool selected = e.Index == tabControl.SelectedIndex;

            using SolidBrush backBrush = new SolidBrush(selected ? RowSelected : SurfaceAlt);
            e.Graphics.FillRectangle(backBrush, e.Bounds);

            using Font tabFont = new Font(FontFamily, 9f, FontStyle.Bold);

            TextRenderer.DrawText(
                e.Graphics,
                page.Text,
                tabFont,
                e.Bounds,
                selected ? Accent : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
