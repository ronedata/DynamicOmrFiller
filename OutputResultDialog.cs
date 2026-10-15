using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DynamicOmrFiller
{
    public class OutputResultDialog : Form
    {
        private readonly string _outputPath;
        private RoundedButton _copyButton;
        private Timer _resetTimer;
        private string _copyButtonOriginalText;

        private readonly int _skippedCount;

        public OutputResultDialog(string outputFolder, int successCount, int failedCount)
            : this(outputFolder, successCount, failedCount, 0)
        {
        }

        public OutputResultDialog(string outputFolder, int successCount, int failedCount, int skippedCount)
        {
            _outputPath = outputFolder ?? string.Empty;
            _skippedCount = skippedCount;
            BuildLayout(successCount, failedCount);
        }

        private void BuildLayout(int successCount, int failedCount)
        {
            bool clean = failedCount == 0;

            Text = "Processing complete";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 226);
            Font = Theme.BodyFont;
            ShowInTaskbar = false;
            BackColor = Theme.Surface;
            KeyPreview = true;

            Label badge = new Label
            {
                Text = clean ? "" : "",
                Font = new Font("Segoe MDL2 Assets", 19F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = clean ? Theme.Success : Theme.Warning,
                AutoSize = true,
                Location = new Point(24, 24)
            };

            Label title = new Label
            {
                Text = clean ? "Processing complete" : "Completed with failures",
                Font = Theme.CardTitleFont,
                ForeColor = Theme.TextPrimary,
                AutoSize = true,
                Location = new Point(66, 24)
            };

            Label summary = new Label
            {
                Text = successCount + " sheet(s) filled · " + failedCount + " failed"
                    + (_skippedCount > 0 ? " · " + _skippedCount + " page(s) skipped" : string.Empty),
                Font = Theme.SmallFont,
                ForeColor = clean ? Theme.TextMuted : Theme.Warning,
                AutoSize = true,
                Location = new Point(66, 47)
            };

            Panel divider = new Panel
            {
                Location = new Point(24, 84),
                Size = new Size(512, 1),
                BackColor = Theme.Border
            };

            Label pathLabel = new Label
            {
                Text = "Output folder",
                Font = Theme.SmallFont,
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Location = new Point(24, 98)
            };

            TextBox pathBox = new TextBox
            {
                Text = _outputPath,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Font = Theme.InputFont
            };
            pathBox.GotFocus += delegate { pathBox.SelectAll(); };
            pathBox.Click += delegate { pathBox.SelectAll(); };

            InputHost pathHost = new InputHost(pathBox, 36, false)
            {
                Dock = DockStyle.None,
                Location = new Point(24, 120),
                Size = new Size(512, 36),
                Margin = new Padding(0)
            };

            _copyButtonOriginalText = "Copy path";
            _copyButton = RoundedButton.Secondary(_copyButtonOriginalText, 110, 34);
            _copyButton.Location = new Point(24, 174);
            _copyButton.Click += CopyButton_Click;

            RoundedButton openButton = RoundedButton.Secondary("Open folder", 118, 34);
            openButton.Location = new Point(142, 174);
            openButton.Click += OpenButton_Click;

            RoundedButton closeButton = RoundedButton.Primary("Close", null, 100, 34);
            closeButton.Location = new Point(436, 174);
            closeButton.DialogResult = DialogResult.OK;

            AcceptButton = closeButton;
            CancelButton = closeButton;

            Controls.Add(badge);
            Controls.Add(title);
            Controls.Add(summary);
            Controls.Add(divider);
            Controls.Add(pathLabel);
            Controls.Add(pathHost);
            Controls.Add(_copyButton);
            Controls.Add(openButton);
            Controls.Add(closeButton);

            // Open with the dialog dismissed by Enter/Esc rather than the path
            // box grabbing focus and showing a selection highlight.
            ActiveControl = closeButton;
        }

        private void CopyButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_outputPath)) return;

            try
            {
                Clipboard.SetText(_outputPath);
            }
            catch
            {
                return;
            }

            _copyButton.Text = "Copied!";
            _copyButton.Enabled = false;

            if (_resetTimer == null)
            {
                _resetTimer = new Timer { Interval = 1400 };
                _resetTimer.Tick += (s, ev) =>
                {
                    _copyButton.Text = _copyButtonOriginalText;
                    _copyButton.Enabled = true;
                    _resetTimer.Stop();
                };
            }

            _resetTimer.Stop();
            _resetTimer.Start();
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(_outputPath)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + _outputPath + "\"",
                UseShellExecute = true
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _resetTimer != null)
            {
                _resetTimer.Dispose();
                _resetTimer = null;
            }
            base.Dispose(disposing);
        }
    }
}
