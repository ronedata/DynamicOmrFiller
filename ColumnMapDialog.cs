using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DynamicOmrFiller
{
    /// <summary>
    /// Lets the user say which spreadsheet column holds the roll numbers and
    /// which holds the registration numbers. Header auto-detection only seeds
    /// the two pickers - the user always gets the final say, and can point at a
    /// column whose name the matcher would never guess.
    /// </summary>
    internal sealed class ColumnMapDialog : Form
    {
        private const string NoneItem = "(do not import)";

        private readonly List<Dictionary<string, string>> _rows;
        private ComboBox _rollCombo;
        private ComboBox _regCombo;
        private Label _rollPreview;
        private Label _regPreview;

        public string RollColumn { get; private set; }
        public string RegistrationColumn { get; private set; }

        public ColumnMapDialog(string fileName, List<Dictionary<string, string>> rows,
            List<string> headers, string suggestedRoll, string suggestedRegistration)
        {
            _rows = rows;
            BuildLayout(fileName, headers, suggestedRoll, suggestedRegistration);
        }

        private void BuildLayout(string fileName, List<string> headers,
            string suggestedRoll, string suggestedRegistration)
        {
            Text = "Choose columns";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 326);
            Font = Theme.BodyFont;
            ShowInTaskbar = false;
            BackColor = Theme.Surface;
            KeyPreview = true;

            Label badge = new Label
            {
                Text = Theme.GlyphOpenFile,
                Font = new Font("Segoe MDL2 Assets", 19F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Theme.Accent,
                AutoSize = true,
                Location = new Point(24, 24)
            };

            Label title = new Label
            {
                Text = "Choose columns",
                Font = Theme.CardTitleFont,
                ForeColor = Theme.TextPrimary,
                AutoSize = true,
                Location = new Point(66, 24)
            };

            Label subtitle = new Label
            {
                Text = fileName + "  ·  " + _rows.Count + " row(s), " + headers.Count + " column(s)",
                Font = Theme.SmallFont,
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Location = new Point(66, 47)
            };

            Panel divider = new Panel
            {
                Location = new Point(24, 84),
                Size = new Size(512, 1),
                BackColor = Theme.Border
            };

            _rollCombo = MakeCombo(headers, suggestedRoll);
            _rollCombo.SelectedIndexChanged += delegate { UpdatePreviews(); };
            _regCombo = MakeCombo(headers, suggestedRegistration);
            _regCombo.SelectedIndexChanged += delegate { UpdatePreviews(); };

            Controls.Add(badge);
            Controls.Add(title);
            Controls.Add(subtitle);
            Controls.Add(divider);

            Controls.Add(MakeCaption("Roll numbers", 24, 100));
            Controls.Add(WrapCombo(_rollCombo, 24, 120));
            _rollPreview = MakePreviewLabel(24, 162);
            Controls.Add(_rollPreview);

            Controls.Add(MakeCaption("Registration numbers", 24, 190));
            Controls.Add(WrapCombo(_regCombo, 24, 210));
            _regPreview = MakePreviewLabel(24, 252);
            Controls.Add(_regPreview);

            RoundedButton cancel = RoundedButton.Secondary("Cancel", 100, 34);
            cancel.Location = new Point(324, 278);
            cancel.DialogResult = DialogResult.Cancel;

            RoundedButton import = RoundedButton.Primary("Import", null, 100, 34);
            import.Location = new Point(436, 278);
            import.Click += ImportButton_Click;

            Controls.Add(cancel);
            Controls.Add(import);

            AcceptButton = import;
            CancelButton = cancel;
            ActiveControl = _rollCombo;

            UpdatePreviews();
        }

        private static ComboBox MakeCombo(List<string> headers, string selected)
        {
            ComboBox combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = Theme.InputFont,
                ForeColor = Theme.TextPrimary,
                BackColor = Theme.Surface
            };

            combo.Items.Add(NoneItem);
            foreach (string header in headers)
            {
                combo.Items.Add(header);
            }

            combo.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(selected))
            {
                int index = combo.Items.IndexOf(selected);
                if (index >= 0) combo.SelectedIndex = index;
            }

            return combo;
        }

        private static InputHost WrapCombo(ComboBox combo, int x, int y)
        {
            return new InputHost(combo, 36, false)
            {
                Dock = DockStyle.None,
                Location = new Point(x, y),
                Size = new Size(512, 36),
                Margin = new Padding(0)
            };
        }

        private static Label MakeCaption(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = Theme.SmallFont,
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Location = new Point(x, y)
            };
        }

        private static Label MakePreviewLabel(int x, int y)
        {
            return new Label
            {
                Font = Theme.SmallFont,
                ForeColor = Theme.TextFaint,
                AutoEllipsis = true,
                AutoSize = false,
                Size = new Size(512, 18),
                Location = new Point(x, y)
            };
        }

        private void UpdatePreviews()
        {
            _rollPreview.Text = Describe(SelectedColumn(_rollCombo));
            _regPreview.Text = Describe(SelectedColumn(_regCombo));
        }

        private static string SelectedColumn(ComboBox combo)
        {
            if (combo.SelectedIndex <= 0) return null;
            return combo.SelectedItem as string;
        }

        /// <summary>Count plus the first few values, so the choice can be checked before importing.</summary>
        private string Describe(string column)
        {
            if (string.IsNullOrEmpty(column)) return "Not imported.";

            List<string> values = ValuesOf(column);
            if (values.Count == 0) return "This column has no values.";

            string sample = string.Join(", ", values.Take(3));
            if (values.Count > 3) sample += ", …";
            return values.Count + " value(s):  " + sample;
        }

        public List<string> ValuesOf(string column)
        {
            if (string.IsNullOrEmpty(column)) return new List<string>();

            return _rows
                .Select(row => row.ContainsKey(column) ? (row[column] ?? string.Empty).Trim() : string.Empty)
                .Where(value => value.Length > 0)
                .ToList();
        }

        private void ImportButton_Click(object sender, EventArgs e)
        {
            string roll = SelectedColumn(_rollCombo);
            string registration = SelectedColumn(_regCombo);

            if (roll == null && registration == null)
            {
                MessageBox.Show(this,
                    "Pick at least one column to import, or press Cancel.",
                    "Dynamic OMR Filler", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            RollColumn = roll;
            RegistrationColumn = registration;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
