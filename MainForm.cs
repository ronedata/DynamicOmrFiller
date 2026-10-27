using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DynamicOmrFiller
{
    public class MainForm : Form
    {
        private static readonly string[] SupportedImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        private const int HeaderHeight = 84;
        private const int FooterHeight = 74;

        private Panel _bodyScroll;
        private TableLayoutPanel _cardStack;
        private bool _initialFitDone;

        private TextBox _inputPath;
        private TextBox _outputPath;
        private TextBox _xmlPath;
        private TextBox _rollNumbers;
        private TextBox _registrationNumbers;
        private Label _studentCountHint;
        private ToggleSwitch _writeStudentNumbers;
        private NumericUpDown _writeOffsetRows;
        private InputHost _writeOffsetHost;
        private Label _writeOffsetHint;
        private TextBox _saqText;
        private TextBox _answerKeyText;
        private Label _answerKeyHint;
        private SegmentedToggle _unlistedAnswers;
        private ToggleSwitch _exportAnswerKey;
        private ToggleSwitch _cropPdfToSheet;
        private NumericUpDown _dotSize;
        private NumericUpDown _saqFontSize;
        private NumericUpDown _saqLabelFontSize;
        private InputHost _saqLabelSizeHost;
        private ToggleSwitch _writeSaqLabel;
        private SegmentedToggle _saqAlignH;
        private SegmentedToggle _saqAlignV;
        private readonly AppSettings _settings = AppSettings.Load();
        private SegmentedToggle _modeToggle;
        private ToggleSwitch _openOutputFolder;
        private ToggleSwitch _fillQuestionQuality;
        private RoundedButton _processButton;
        private RoundedButton _inputBrowse;
        private Label _inputLabel;
        private Label _dotSizeHint;
        private Label _saqFontHint;
        private Label _statusLabel;
        private StatusDot _statusDot;
        private SlimProgress _progress;
        private readonly ToolTip _toolTip = new ToolTip();

        private readonly OmrProcessor _processor;

        public MainForm()
        {
            _processor = new OmrProcessor();
            _processor.LogMessage += Processor_LogMessage;

            BuildLayout();
            SeedDefaults();
            UpdateDotSizeHint();
            UpdateSaqFontHint();
            UpdateAnswerKeyHint();
            UpdateStudentCountHint();
            UpdateWriteOffsetHint();
            UpdateInputModeUi();
        }

        private OmrInputMode SelectedInputMode
        {
            get
            {
                if (_modeToggle.SelectedIndex == 1) return OmrInputMode.Folder;
                if (_modeToggle.SelectedIndex == 2) return OmrInputMode.Pdf;
                return OmrInputMode.SingleImage;
            }
        }

        private static string GetAppVersion()
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return "v" + v.Major + "." + v.Minor + "." + v.Build;
        }

        // ------------------------------------------------------------------
        // Shell
        // ------------------------------------------------------------------

        private void BuildLayout()
        {
            string version = GetAppVersion();
            Text = "Dynamic OMR Filler — " + version;

            Image appImage = null;
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
                appImage = Icon.ToBitmap();
            }

            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            MinimumSize = new Size(900, 360);
            Size = new Size(990, 760);
            Font = Theme.BodyFont;
            BackColor = Theme.PageBack;
            DoubleBuffered = true;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.PageBack,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));

            root.Controls.Add(BuildHeader(version, appImage), 0, 0);
            root.Controls.Add(BuildBody(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);

            Controls.Add(root);
        }

        private Control BuildHeader(string version, Image appImage)
        {
            EdgePanel header = new EdgePanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                BottomBorder = Theme.Border,
                Margin = new Padding(0)
            };

            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Surface,
                Padding = new Padding(24, 0, 24, 0),
                Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            IconTile tile = new IconTile(appImage) { Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 14, 0) };
            grid.Controls.Add(tile, 0, 0);

            TableLayoutPanel text = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Surface,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0)
            };
            text.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            text.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            text.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            text.Controls.Add(new Label
            {
                Text = "Dynamic OMR Filler",
                Font = Theme.AppTitleFont,
                ForeColor = Theme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0)
            }, 0, 0);
            text.Controls.Add(new Label
            {
                Text = "Fill blank OMR sheets with synthetic roll, registration and answer data",
                Font = Theme.SmallFont,
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 0)
            }, 0, 1);
            grid.Controls.Add(text, 1, 0);

            PillLabel versionPill = new PillLabel
            {
                Text = version,
                Size = new Size(60, 23),
                Anchor = AnchorStyles.None,
                Margin = new Padding(0)
            };
            grid.Controls.Add(versionPill, 2, 0);

            header.Controls.Add(grid);
            return header;
        }

        private Control BuildBody()
        {
            Panel scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.PageBack,
                Padding = new Padding(24, 18, 24, 18),
                Margin = new Padding(0)
            };

            TableLayoutPanel stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 5,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.PageBack,
                Margin = new Padding(0)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 5; i++)
            {
                stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            stack.Controls.Add(BuildSourceCard(), 0, 0);
            stack.Controls.Add(BuildStudentCard(), 0, 1);
            stack.Controls.Add(BuildAnswerKeyCard(), 0, 2);
            stack.Controls.Add(BuildSaqCard(), 0, 3);
            stack.Controls.Add(BuildSettingsCard(), 0, 4);

            // Collapsing or expanding a card re-fits the window height, so the
            // shell never leaves a band of dead space under the last card.
            foreach (Control child in stack.Controls)
            {
                CardPanel card = child as CardPanel;
                if (card != null) card.CollapsedChanged += delegate { FitToContent(); };
            }

            scroll.Controls.Add(stack);
            _bodyScroll = scroll;
            _cardStack = stack;
            return scroll;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitToContent();
        }

        /// <summary>
        /// Sizes the window height to exactly what the cards need, clamped to
        /// the working area (beyond that the body scrolls) and to MinimumSize.
        /// Width is left alone - only the vertical dead space is the problem.
        /// </summary>
        private void FitToContent()
        {
            if (_cardStack == null || _bodyScroll == null) return;
            if (WindowState != FormWindowState.Normal) return;

            int content = _cardStack.PreferredSize.Height + _bodyScroll.Padding.Vertical;
            int chrome = (Height - ClientSize.Height) + HeaderHeight + FooterHeight;

            Rectangle working = Screen.FromControl(this).WorkingArea;
            int target = Math.Min(content + chrome, working.Height - 40);
            target = Math.Max(MinimumSize.Height, target);

            if (Math.Abs(Height - target) > 2)
            {
                Height = target;
            }

            if (!_initialFitDone)
            {
                // CenterScreen positioned us for the pre-fit height; recentre.
                _initialFitDone = true;
                Top = working.Top + Math.Max(0, (working.Height - Height) / 2);
            }
        }

        private Control BuildFooter()
        {
            EdgePanel footer = new EdgePanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                TopBorder = Theme.Border,
                Margin = new Padding(0)
            };

            TableLayoutPanel outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Surface,
                Margin = new Padding(0)
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 3F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _progress = new SlimProgress { Dock = DockStyle.Fill, Margin = new Padding(0) };
            outer.Controls.Add(_progress, 0, 0);

            TableLayoutPanel bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Surface,
                Padding = new Padding(24, 0, 24, 0),
                Margin = new Padding(0)
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _statusDot = new StatusDot { Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 8, 0) };
            bar.Controls.Add(_statusDot, 0, 0);

            _statusLabel = new Label
            {
                Text = "Ready",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.StatusFont,
                ForeColor = Theme.TextMuted,
                AutoEllipsis = true,
                Margin = new Padding(0)
            };
            bar.Controls.Add(_statusLabel, 1, 0);

            _processButton = RoundedButton.Primary("Process OMR", Theme.GlyphPlay, 178, 40);
            _processButton.Anchor = AnchorStyles.None;
            _processButton.Margin = new Padding(0);
            _processButton.Click += ProcessButton_Click;
            bar.Controls.Add(_processButton, 2, 0);

            outer.Controls.Add(bar, 0, 1);
            footer.Controls.Add(outer);
            return footer;
        }

        // ------------------------------------------------------------------
        // Cards
        // ------------------------------------------------------------------

        private Control BuildSourceCard()
        {
            CardPanel card = new CardPanel(Theme.GlyphFolder, "Source files",
                "Where the blank sheets come from and where the filled copies are written");

            TableLayoutPanel grid = CardPanel.MakeGrid(3);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));

            int row = AddRow(grid);
            grid.Controls.Add(MakeFieldLabel("Mode"), 0, row);
            _modeToggle = new SegmentedToggle(new[] { "Single image", "Folder batch", "PDF file" }, 124);
            _modeToggle.SelectedIndexChanged += delegate { UpdateInputModeUi(); };
            grid.SetColumnSpan(_modeToggle, 2);
            grid.Controls.Add(_modeToggle, 1, row);

            row = AddRow(grid);
            _inputLabel = MakeFieldLabel("Input file");
            _inputPath = MakeTextBox();
            InputHost inputHost = MakeHost(_inputPath);
            _inputBrowse = MakeBrowseButton(BrowseInputPath);
            EnableFileDrop(inputHost, _inputPath, ResolveInputDrop);
            EnableFileDrop(_inputPath, _inputPath, ResolveInputDrop);
            grid.Controls.Add(_inputLabel, 0, row);
            grid.Controls.Add(inputHost, 1, row);
            grid.Controls.Add(_inputBrowse, 2, row);
            _toolTip.SetToolTip(_inputPath, "Drag and drop the image or folder here.");

            row = AddRow(grid);
            _outputPath = MakeTextBox();
            InputHost outputHost = MakeHost(_outputPath);
            EnableFileDrop(outputHost, _outputPath, ResolveOutputDrop);
            EnableFileDrop(_outputPath, _outputPath, ResolveOutputDrop);
            grid.Controls.Add(MakeFieldLabel("Output folder"), 0, row);
            grid.Controls.Add(outputHost, 1, row);
            grid.Controls.Add(MakeBrowseButton(BrowseOutputFolder), 2, row);
            _toolTip.SetToolTip(_outputPath, "Leave empty to auto-create a 'FillUpOMR' folder inside the input directory.");

            row = AddRow(grid);
            Label outputHint = MakeHintLabel("Leave empty and a 'FillUpOMR' folder is created inside the input directory.");
            grid.SetColumnSpan(outputHint, 2);
            grid.Controls.Add(outputHint, 1, row);

            row = AddRow(grid);
            _xmlPath = MakeTextBox();
            InputHost xmlHost = MakeHost(_xmlPath);
            EnableFileDrop(xmlHost, _xmlPath, ResolveXmlDrop);
            EnableFileDrop(_xmlPath, _xmlPath, ResolveXmlDrop);
            grid.Controls.Add(MakeFieldLabel("XML config"), 0, row);
            grid.Controls.Add(xmlHost, 1, row);
            grid.Controls.Add(MakeBrowseButton(BrowseXmlFile), 2, row);
            _toolTip.SetToolTip(_xmlPath, "Grid definition XML describing the regions on each page.");

            card.Add(grid);
            return card;
        }

        private Control BuildStudentCard()
        {
            CardPanel card = new CardPanel(Theme.GlyphContact, "Student information",
                "Roll and registration numbers mapped onto the bubble grid, in order");

            TableLayoutPanel grid = CardPanel.MakeGrid(4);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            int row = AddRow(grid);
            _rollNumbers = MakeTextArea();
            _registrationNumbers = MakeTextArea();
            grid.Controls.Add(MakeFieldLabel("Roll numbers"), 0, row);
            grid.Controls.Add(new InputHost(_rollNumbers, 92, true), 1, row);
            grid.Controls.Add(MakeFieldLabel("Registration"), 2, row);
            grid.Controls.Add(new InputHost(_registrationNumbers, 92, true), 3, row);
            _toolTip.SetToolTip(_rollNumbers,
                "One roll per line, or comma-separated. One roll is consumed per sheet detected as 'Page 1'.");
            _toolTip.SetToolTip(_registrationNumbers,
                "One registration per line, or comma-separated. Paired with the roll numbers by position.");
            _rollNumbers.TextChanged += delegate { UpdateStudentCountHint(); };
            _registrationNumbers.TextChanged += delegate { UpdateStudentCountHint(); };

            row = AddRow(grid);
            _studentCountHint = MakeHintLabel(string.Empty);
            grid.SetColumnSpan(_studentCountHint, 3);
            grid.Controls.Add(_studentCountHint, 1, row);

            row = AddRow(grid);
            _writeStudentNumbers = new ToggleSwitch
            {
                Text = "Also write the digits in the boxes above the bubbles",
                Checked = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 2)
            };
            _toolTip.SetToolTip(_writeStudentNumbers,
                "Prints each digit in the write-in box over its own bubble column, the way a candidate fills the sheet by hand.");
            grid.Controls.Add(MakeFieldLabel("Write numbers"), 0, row);
            grid.SetColumnSpan(_writeStudentNumbers, 3);
            grid.Controls.Add(_writeStudentNumbers, 1, row);

            row = AddRow(grid);
            _writeOffsetRows = MakeDecimalNumeric(0m, 20m, 1.0m, 1, 0.1m);
            _writeOffsetRows.ValueChanged += delegate { UpdateWriteOffsetHint(); };
            _writeOffsetHost = MakeHost(_writeOffsetRows);
            // A one-decimal number does not need half the card's width.
            _writeOffsetHost.Dock = DockStyle.None;
            _writeOffsetHost.Anchor = AnchorStyles.Left;
            _writeOffsetHost.Width = 124;
            _writeOffsetHint = MakeHintLabel(string.Empty);
            _toolTip.SetToolTip(_writeOffsetRows,
                "How far above the first bubble row the digits are printed, measured in grid rows."
                + Environment.NewLine
                + "1.0 lands in the printed box row. Raise it to move the digits up, lower it to move them down.");
            grid.Controls.Add(MakeFieldLabel("Write offset"), 0, row);
            grid.Controls.Add(_writeOffsetHost, 1, row);
            grid.SetColumnSpan(_writeOffsetHint, 2);
            grid.Controls.Add(_writeOffsetHint, 2, row);

            _writeStudentNumbers.CheckedChanged += delegate
            {
                _writeOffsetHost.Enabled = _writeStudentNumbers.Checked;
                UpdateWriteOffsetHint();
            };

            row = AddRow(grid);
            RoundedButton loadButton = RoundedButton.Soft("Load from Excel / CSV", Theme.GlyphOpenFile, 212, 34);
            loadButton.Anchor = AnchorStyles.Left;
            loadButton.Margin = new Padding(0, 6, 0, 2);
            loadButton.Click += LoadStudentDataFromFile;
            _toolTip.SetToolTip(loadButton,
                "Pick a .xlsx or .csv, then choose which column holds the roll numbers and which holds the registrations.");
            grid.Controls.Add(MakeFieldLabel("Bulk import"), 0, row);
            grid.SetColumnSpan(loadButton, 3);
            grid.Controls.Add(loadButton, 1, row);

            row = AddRow(grid);
            Label importHint = MakeHintLabel(
                "One value per line, or comma-separated. Importing lets you pick the roll and registration columns yourself.");
            grid.SetColumnSpan(importHint, 3);
            grid.Controls.Add(importHint, 1, row);

            row = AddRow(grid);
            RoundedButton saveStudents = RoundedButton.Soft("Save as default", Theme.GlyphSave, 176, 32);
            saveStudents.Anchor = AnchorStyles.Left;
            saveStudents.Margin = new Padding(0, 6, 0, 2);
            saveStudents.Click += SaveStudentDefaults;
            _toolTip.SetToolTip(saveStudents,
                "Remembers these roll and registration numbers and loads them the next time the app starts.");
            grid.SetColumnSpan(saveStudents, 3);
            grid.Controls.Add(MakeFieldLabel("Defaults"), 0, row);
            grid.Controls.Add(saveStudents, 1, row);

            card.Add(grid);
            card.Collapsed = true;
            return card;
        }

        private Control BuildAnswerKeyCard()
        {
            CardPanel card = new CardPanel(Theme.GlyphChecklist, "MCQ answer key",
                "Per-question answers such as 1-A,2-AC,3-B - several letters bubble several options");

            TableLayoutPanel grid = CardPanel.MakeGrid(2);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int row = AddRow(grid);
            _answerKeyText = MakeTextArea();
            _answerKeyText.TextChanged += delegate { UpdateAnswerKeyHint(); };
            InputHost keyHost = new InputHost(_answerKeyText, 74, true);
            grid.Controls.Add(MakeFieldLabel("Answer key"), 0, row);
            grid.Controls.Add(keyHost, 1, row);
            _toolTip.SetToolTip(_answerKeyText,
                "1-A,2-AC,3-B,4-ABCD  ·  an empty slot (,,) blanks that question  ·  plain A,C,B,D also works."
                + Environment.NewLine
                + "Applies to 'answer*' regions only - roll, registration and SAQ are untouched.");

            row = AddRow(grid);
            _unlistedAnswers = new SegmentedToggle(new[] { "Fill randomly", "Leave blank" }, 118);
            _unlistedAnswers.SelectedIndexChanged += delegate { UpdateAnswerKeyHint(); };
            grid.Controls.Add(MakeFieldLabel("Other questions"), 0, row);
            grid.Controls.Add(_unlistedAnswers, 1, row);
            _toolTip.SetToolTip(_unlistedAnswers,
                "What happens to the questions the key does not list.");

            row = AddRow(grid);
            _answerKeyHint = MakeHintLabel(string.Empty);
            grid.Controls.Add(_answerKeyHint, 1, row);

            card.Add(grid);
            card.Collapsed = true;
            return card;
        }

        private Control BuildSaqCard()
        {
            CardPanel card = new CardPanel(Theme.GlyphDocument, "SAQ content",
                "Text rendered into short-answer cells, above the auto-generated cell label");

            TableLayoutPanel grid = CardPanel.MakeGrid(3);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int row = AddRow(grid);
            _saqFontSize = MakeNumeric(0, 200, 0);
            _saqFontSize.ValueChanged += delegate { UpdateSaqFontHint(); };
            _saqFontHint = MakeHintLabel(string.Empty);
            grid.Controls.Add(MakeFieldLabel("Text size (px)"), 0, row);
            grid.Controls.Add(MakeHost(_saqFontSize), 1, row);
            grid.Controls.Add(_saqFontHint, 2, row);

            row = AddRow(grid);
            _writeSaqLabel = new ToggleSwitch
            {
                Text = "Write the auto-generated cell label (SaqAnswer1)",
                Checked = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 2)
            };
            _toolTip.SetToolTip(_writeSaqLabel,
                "The small label under the SAQ text, taken from the region's memberName."
                + Environment.NewLine
                + "Turn it off and the SAQ text gets the whole cell.");
            grid.Controls.Add(MakeFieldLabel("Cell label"), 0, row);
            grid.SetColumnSpan(_writeSaqLabel, 2);
            grid.Controls.Add(_writeSaqLabel, 1, row);

            row = AddRow(grid);
            _saqLabelFontSize = MakeNumeric(0, 200, 0);
            _saqLabelFontSize.ValueChanged += delegate { UpdateSaqFontHint(); };
            _saqLabelSizeHost = MakeHost(_saqLabelFontSize);
            grid.Controls.Add(MakeFieldLabel("Label size (px)"), 0, row);
            grid.Controls.Add(_saqLabelSizeHost, 1, row);
            grid.Controls.Add(MakeHintLabel("Size of the auto-generated cell label (SaqAnswer1). 0 = auto."), 2, row);
            _toolTip.SetToolTip(_saqLabelFontSize,
                "The small label printed under the SAQ text, taken from the region's memberName.");

            // The size only means anything while the label is being written.
            _writeSaqLabel.CheckedChanged += delegate
            {
                _saqLabelSizeHost.Enabled = _writeSaqLabel.Checked;
            };

            row = AddRow(grid);
            FlowLayoutPanel alignRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 2, 0, 2),
                Padding = new Padding(0)
            };
            _saqAlignH = new SegmentedToggle(new[] { "Left", "Centre", "Right" }, 84);
            _saqAlignV = new SegmentedToggle(new[] { "Top", "Middle", "Bottom" }, 84);
            _saqAlignH.SelectedIndex = 1;
            _saqAlignV.SelectedIndex = 1;
            _saqAlignH.Margin = new Padding(0, 0, 14, 0);
            alignRow.Controls.Add(_saqAlignH);
            alignRow.Controls.Add(_saqAlignV);
            grid.SetColumnSpan(alignRow, 2);
            grid.Controls.Add(MakeFieldLabel("Text position"), 0, row);
            grid.Controls.Add(alignRow, 1, row);

            row = AddRow(grid);
            _saqText = MakeTextArea();
            InputHost saqHost = new InputHost(_saqText, 92, true);
            grid.SetColumnSpan(saqHost, 2);
            grid.Controls.Add(MakeFieldLabel("SAQ text"), 0, row);
            grid.Controls.Add(saqHost, 1, row);

            row = AddRow(grid);
            RoundedButton saveSaq = RoundedButton.Soft("Save as default", Theme.GlyphSave, 176, 32);
            saveSaq.Anchor = AnchorStyles.Left;
            saveSaq.Margin = new Padding(0, 6, 0, 2);
            saveSaq.Click += SaveSaqDefaults;
            _toolTip.SetToolTip(saveSaq,
                "Remembers this text, both sizes and the position, and loads them the next time the app starts.");
            grid.SetColumnSpan(saveSaq, 2);
            grid.Controls.Add(MakeFieldLabel("Defaults"), 0, row);
            grid.Controls.Add(saveSaq, 1, row);

            card.Add(grid);
            card.Collapsed = true;
            return card;
        }

        private Control BuildSettingsCard()
        {
            CardPanel card = new CardPanel(Theme.GlyphSettings, "Settings",
                "How the bubbles are drawn and what happens once the batch finishes");

            TableLayoutPanel grid = CardPanel.MakeGrid(3);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int row = AddRow(grid);
            _dotSize = MakeNumeric(1, 200, 30);
            _dotSize.ValueChanged += delegate { UpdateDotSizeHint(); };
            _dotSizeHint = MakeHintLabel(string.Empty);
            grid.Controls.Add(MakeFieldLabel("Dot size (px)"), 0, row);
            grid.Controls.Add(MakeHost(_dotSize), 1, row);
            grid.Controls.Add(_dotSizeHint, 2, row);

            row = AddRow(grid);
            _fillQuestionQuality = new ToggleSwitch
            {
                Text = "Fill Question Quality randomly (S / H / E from XML)",
                Checked = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 0, 2)
            };
            grid.Controls.Add(MakeFieldLabel("Fill options"), 0, row);
            grid.SetColumnSpan(_fillQuestionQuality, 2);
            grid.Controls.Add(_fillQuestionQuality, 1, row);

            row = AddRow(grid);
            _cropPdfToSheet = new ToggleSwitch
            {
                Text = "Crop PDF pages to the OMR sheet (drop the question half)",
                Checked = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 0, 2)
            };
            _toolTip.SetToolTip(_cropPdfToSheet,
                "A PDF page is the whole A4 sheet. With this on, the output keeps only the OMR block, matching the pre-cropped images.");
            grid.Controls.Add(MakeFieldLabel("PDF output"), 0, row);
            grid.SetColumnSpan(_cropPdfToSheet, 2);
            grid.Controls.Add(_cropPdfToSheet, 1, row);

            row = AddRow(grid);
            _exportAnswerKey = new ToggleSwitch
            {
                Text = "Export the filled answers as an answer_key CSV",
                Checked = false,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 0, 2)
            };
            grid.Controls.Add(MakeFieldLabel("Export"), 0, row);
            grid.SetColumnSpan(_exportAnswerKey, 2);
            grid.Controls.Add(_exportAnswerKey, 1, row);
            _toolTip.SetToolTip(_exportAnswerKey,
                "Writes answer_key_<timestamp>.csv into the output folder: file, roll, registration, page, question, answer.");

            row = AddRow(grid);
            _openOutputFolder = new ToggleSwitch
            {
                Text = "Open the output folder when processing finishes",
                Checked = false,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 0, 2)
            };
            grid.Controls.Add(MakeFieldLabel("After process"), 0, row);
            grid.SetColumnSpan(_openOutputFolder, 2);
            grid.Controls.Add(_openOutputFolder, 1, row);

            card.Add(grid);
            card.Collapsed = true;
            return card;
        }

        // ------------------------------------------------------------------
        // Small builders
        // ------------------------------------------------------------------

        private static int AddRow(TableLayoutPanel grid)
        {
            int row = grid.RowCount;
            grid.RowCount = row + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return row;
        }

        private static Label MakeFieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Theme.BodyFont,
                ForeColor = Theme.TextBody,
                Margin = new Padding(0, 4, 10, 4)
            };
        }

        private static Label MakeHintLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Theme.SmallFont,
                ForeColor = Theme.TextFaint,
                Margin = new Padding(0, 2, 0, 6)
            };
        }

        private static TextBox MakeTextBox()
        {
            return new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Theme.InputFont
            };
        }

        /// <summary>Multiline input, used for every list-style field.</summary>
        private static TextBox MakeTextArea()
        {
            return new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                AcceptsReturn = true,
                Font = Theme.InputFont
            };
        }

        /// <summary>Fractional spin box, used for the write offset in grid rows.</summary>
        private static NumericUpDown MakeDecimalNumeric(decimal min, decimal max, decimal value,
            int decimals, decimal increment)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                DecimalPlaces = decimals,
                Increment = increment,
                BorderStyle = BorderStyle.None,
                Font = Theme.InputFont
            };
        }

        private static NumericUpDown MakeNumeric(int min, int max, int value)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                BorderStyle = BorderStyle.None,
                Font = Theme.InputFont
            };
        }

        private static InputHost MakeHost(Control inner)
        {
            return new InputHost(inner, 36, false);
        }

        private static RoundedButton MakeBrowseButton(EventHandler onClick)
        {
            RoundedButton button = RoundedButton.Secondary("Browse", 104, 36);
            button.Anchor = AnchorStyles.Left;
            button.Margin = new Padding(0, 4, 0, 4);
            button.Click += onClick;
            return button;
        }

        private static void EnableFileDrop(Control target, TextBox box, Func<string[], string> resolver)
        {
            target.AllowDrop = true;
            target.DragEnter += (s, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.None;
                    return;
                }
                string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                e.Effect = !string.IsNullOrEmpty(resolver(paths)) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            target.DragDrop += (s, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                string resolved = resolver(paths);
                if (!string.IsNullOrEmpty(resolved))
                {
                    box.Text = resolved;
                }
            };
        }

        private void SeedDefaults()
        {
            if (_settings.HasStudentDefaults)
            {
                _rollNumbers.Text = _settings.RollNumbers ?? string.Empty;
                _registrationNumbers.Text = _settings.RegistrationNumbers ?? string.Empty;
                _writeStudentNumbers.Checked = _settings.WriteStudentNumbers;
                _writeOffsetRows.Value = ClampToRange((decimal)_settings.WriteOffsetRows, _writeOffsetRows);
            }
            else
            {
                _rollNumbers.Text = "10205026680,10205026681";
                _registrationNumbers.Text = "1607495,1607496";
            }

            if (_settings.HasSaqDefaults)
            {
                _saqText.Text = _settings.SaqText ?? string.Empty;
                _saqFontSize.Value = ClampToRange(_settings.SaqFontSizePixels, _saqFontSize);
                _saqLabelFontSize.Value = ClampToRange(_settings.SaqLabelFontSizePixels, _saqLabelFontSize);
                _writeSaqLabel.Checked = _settings.WriteSaqLabel;
                _saqLabelSizeHost.Enabled = _settings.WriteSaqLabel;
                _saqAlignH.SelectedIndex = FromAlignment(_settings.SaqAlignHorizontal);
                _saqAlignV.SelectedIndex = FromAlignment(_settings.SaqAlignVertical);
                return;
            }

            _saqText.Text = "ইরানের নতুন সর্বোচ্চ নেতা মোজতবা খামেনি যুদ্ধকালীন চোটে আহত হলেও বর্তমানে ‘নিরাপদ আছেন।\\r\\n\\r\\nআজ বুধবার ইরানের প্রেসিডেন্ট মাসুদ পেজেশকিয়ানের ছেলে ও সরকারের উপদেষ্টা ইউসেফ পেজেশকিয়ান এ তথ্য নিশ্চিত করেছেন।\\r\\n\\r\\nনিজের টেলিগ্রাম চ্যানেলে দেওয়া এক পোস্টে ইউসেফ পেজেশকিয়ান বলেন, ‘আমি খবর পেয়েছিলাম যে মোজতবা খামেনি আহত হয়েছেন। বিষয়টি নিয়ে সংশ্লিষ্টদের সঙ্গে যোগাযোগ করলে তাঁরা আমাকে জানিয়েছেন, আল্লাহর রহমতে তিনি নিরাপদ ও সুস্থ আছেন।’".Replace("\\r\\n", Environment.NewLine);
        }

        private static decimal ClampToRange(decimal value, NumericUpDown box)
        {
            if (value < box.Minimum) return box.Minimum;
            if (value > box.Maximum) return box.Maximum;
            return value;
        }

        private static StringAlignment ToAlignment(int index)
        {
            if (index == 0) return StringAlignment.Near;
            if (index == 2) return StringAlignment.Far;
            return StringAlignment.Center;
        }

        private static int FromAlignment(StringAlignment alignment)
        {
            if (alignment == StringAlignment.Near) return 0;
            if (alignment == StringAlignment.Far) return 2;
            return 1;
        }

        private void SaveStudentDefaults(object sender, EventArgs e)
        {
            _settings.RollNumbers = _rollNumbers.Text;
            _settings.RegistrationNumbers = _registrationNumbers.Text;
            _settings.WriteStudentNumbers = _writeStudentNumbers.Checked;
            _settings.WriteOffsetRows = (float)_writeOffsetRows.Value;
            _settings.HasStudentDefaults = true;
            PersistSettings(sender as RoundedButton, "roll and registration numbers");
        }

        private void SaveSaqDefaults(object sender, EventArgs e)
        {
            _settings.SaqText = _saqText.Text;
            _settings.SaqFontSizePixels = (int)_saqFontSize.Value;
            _settings.SaqLabelFontSizePixels = (int)_saqLabelFontSize.Value;
            _settings.WriteSaqLabel = _writeSaqLabel.Checked;
            _settings.SaqAlignHorizontal = ToAlignment(_saqAlignH.SelectedIndex);
            _settings.SaqAlignVertical = ToAlignment(_saqAlignV.SelectedIndex);
            _settings.HasSaqDefaults = true;
            PersistSettings(sender as RoundedButton, "SAQ content");
        }

        private void PersistSettings(RoundedButton button, string what)
        {
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                _statusDot.DotColor = Theme.Danger;
                SetStatus("Could not save defaults: " + ex.Message);
                MessageBox.Show(this, "Could not save the defaults." + Environment.NewLine + Environment.NewLine
                    + ex.Message, "Dynamic OMR Filler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _statusDot.DotColor = Theme.Success;
            SetStatus("Saved the " + what + " as defaults - " + AppSettings.FilePath);
            FlashSaved(button);
        }

        /// <summary>Brief "Saved!" confirmation on the button that was pressed.</summary>
        private void FlashSaved(RoundedButton button)
        {
            if (button == null) return;

            string original = button.Text;
            button.Text = "Saved!";
            button.Enabled = false;

            Timer timer = new Timer { Interval = 1400 };
            timer.Tick += (s, e) =>
            {
                button.Text = original;
                button.Enabled = true;
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        }

        /// <summary>
        /// Shows the answer key's format problems before a batch starts, so a
        /// typo like "x-A" is caught instead of quietly bubbling nonsense.
        /// </summary>
        private bool ConfirmAnswerKeyProblems()
        {
            List<string> problems;
            OmrProcessor.ParseAnswerKey(_answerKeyText.Text, out problems);
            if (problems.Count == 0) return true;

            string details = string.Join(Environment.NewLine,
                problems.Take(8).Select(problem => "   \u2022 " + problem));
            if (problems.Count > 8)
            {
                details += Environment.NewLine + "   \u2022 ... and " + (problems.Count - 8) + " more.";
            }

            string message = "The MCQ answer key has " + problems.Count + " problem(s):"
                + Environment.NewLine + Environment.NewLine + details
                + Environment.NewLine + Environment.NewLine
                + "Expected format:  1-A,2-AC,3-B,4-ABCD" + Environment.NewLine + Environment.NewLine
                + "Process anyway?";

            return MessageBox.Show(this, message, "Dynamic OMR Filler",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        // ------------------------------------------------------------------
        // Behaviour
        // ------------------------------------------------------------------

        private async void ProcessButton_Click(object sender, EventArgs e)
        {
            if (!ConfirmAnswerKeyProblems()) return;

            try
            {
                SetBusyState(true);
                OmrBatchOptions options = CollectOptions();

                OmrBatchResult result = await Task.Run(() => _processor.ProcessBatch(options));
                _statusDot.DotColor = result.FailureCount == 0 ? Theme.Success : Theme.Warning;
                _statusLabel.Text = "Done · Success: " + result.SuccessCount + ", Failed: " + result.FailureCount
                    + (result.SkippedCount > 0 ? ", Skipped: " + result.SkippedCount : string.Empty)
                    + (string.IsNullOrEmpty(result.AnswerKeyFile) ? string.Empty : " · Key: " + Path.GetFileName(result.AnswerKeyFile))
                    + " · Output: " + options.OutputFolder;

                if (_openOutputFolder.Checked)
                {
                    OpenOutputFolder(options.OutputFolder);
                }

                using (OutputResultDialog dlg = new OutputResultDialog(options.OutputFolder,
                    result.SuccessCount, result.FailureCount, result.SkippedCount))
                {
                    dlg.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                _statusDot.DotColor = Theme.Danger;
                _statusLabel.Text = "Error: " + ex.Message;
                MessageBox.Show(this, ex.Message, "Dynamic OMR Filler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private OmrBatchOptions CollectOptions()
        {
            return new OmrBatchOptions
            {
                InputPath = _inputPath.Text.Trim(),
                OutputFolder = _outputPath.Text.Trim(),
                XmlPath = _xmlPath.Text.Trim(),
                RollNumbersInput = _rollNumbers.Text,
                RegistrationNumbersInput = _registrationNumbers.Text,
                SaqExtraText = _saqText.Text,
                InputMode = SelectedInputMode,
                DotSizePixels = (int)_dotSize.Value,
                SaqFontSizePixels = (int)_saqFontSize.Value,
                SaqLabelFontSizePixels = (int)_saqLabelFontSize.Value,
                WriteSaqLabel = _writeSaqLabel.Checked,
                SaqAlignHorizontal = ToAlignment(_saqAlignH.SelectedIndex),
                SaqAlignVertical = ToAlignment(_saqAlignV.SelectedIndex),
                FillQuestionQuality = _fillQuestionQuality.Checked,
                WriteStudentNumbers = _writeStudentNumbers.Checked,
                CropPdfToSheet = _cropPdfToSheet.Checked,
                WriteOffsetRows = (float)_writeOffsetRows.Value,
                AnswerKeyInput = _answerKeyText.Text,
                ExportAnswerKey = _exportAnswerKey.Checked,
                UnlistedAnswersRandom = _unlistedAnswers.SelectedIndex == 0
            };
        }

        private void SetBusyState(bool isBusy)
        {
            _processButton.Enabled = !isBusy;
            _processButton.Text = isBusy ? "Processing…" : "Process OMR";
            UseWaitCursor = isBusy;

            if (isBusy)
            {
                _statusDot.DotColor = Theme.Accent;
                _statusLabel.Text = "Processing…";
                _progress.Start();
            }
            else
            {
                _progress.Stop();
            }
        }

        private void Processor_LogMessage(object sender, OmrLogEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), e.Message);
                return;
            }
            SetStatus(e.Message);
        }

        private void SetStatus(string message)
        {
            _statusLabel.Text = message;
        }

        private void LoadStudentDataFromFile(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Excel/CSV files|*.xlsx;*.csv|Excel files|*.xlsx|CSV files|*.csv|All files|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    List<string> headers;
                    List<Dictionary<string, string>> rows = ExcelLoader.LoadRows(dlg.FileName, out headers);

                    if (headers.Count == 0 || rows.Count == 0)
                    {
                        MessageBox.Show(this,
                            "That file has no header row or no data rows to import.",
                            "Dynamic OMR Filler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Auto-detection only seeds the pickers; the user decides.
                    string rollHeader = FindMatchingHeader(rows, new[] { "Roll Number", "Roll No", "RollNumber", "RollNo", "Roll" });
                    string regHeader = FindMatchingHeader(rows, new[] { "Registration Number", "Registration No", "RegistrationNumber", "RegistrationNo", "Registration", "Reg Number", "Reg No", "Reg" });

                    string rollValues;
                    string regValues;
                    int rollCount;
                    int regCount;

                    using (ColumnMapDialog picker = new ColumnMapDialog(
                        Path.GetFileName(dlg.FileName), rows, headers, rollHeader, regHeader))
                    {
                        if (picker.ShowDialog(this) != DialogResult.OK) return;

                        List<string> rollList = picker.ValuesOf(picker.RollColumn);
                        List<string> regList = picker.ValuesOf(picker.RegistrationColumn);
                        rollValues = string.Join(",", rollList);
                        regValues = string.Join(",", regList);
                        rollCount = rollList.Count;
                        regCount = regList.Count;

                        // A column the user did not pick leaves that box alone.
                        if (picker.RollColumn != null) _rollNumbers.Text = rollValues;
                        if (picker.RegistrationColumn != null) _registrationNumbers.Text = regValues;
                    }

                    _statusDot.DotColor = rollCount != regCount && rollCount > 0 && regCount > 0 ? Theme.Warning : Theme.Success;
                    SetStatus("Loaded " + rollCount + " roll(s) and " + regCount + " registration(s) from " + Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex)
                {
                    _statusDot.DotColor = Theme.Danger;
                    MessageBox.Show(this, ex.Message, "Dynamic OMR Filler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string FindMatchingHeader(List<Dictionary<string, string>> rows, string[] candidates)
        {
            if (rows.Count == 0) return null;
            Dictionary<string, string> firstRow = rows[0];
            foreach (string candidate in candidates)
            {
                string normalized = NormalizeHeader(candidate);
                foreach (string key in firstRow.Keys)
                {
                    if (NormalizeHeader(key) == normalized) return key;
                }
            }
            return null;
        }

        private static string NormalizeHeader(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private void BrowseInputPath(object sender, EventArgs e)
        {
            OmrInputMode mode = SelectedInputMode;

            if (mode != OmrInputMode.Folder)
            {
                using (OpenFileDialog fileDialog = new OpenFileDialog())
                {
                    fileDialog.Filter = mode == OmrInputMode.Pdf
                        ? "PDF files|*.pdf|All files|*.*"
                        : "Image files|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*";
                    fileDialog.CheckFileExists = true;
                    if (fileDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        _inputPath.Text = fileDialog.FileName;
                    }
                }
                return;
            }

            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    _inputPath.Text = folderDialog.SelectedPath;
                }
            }
        }

        private void BrowseOutputFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                if (Directory.Exists(_outputPath.Text))
                {
                    dialog.SelectedPath = _outputPath.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _outputPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void BrowseXmlFile(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "XML files|*.xml|All files|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _xmlPath.Text = dialog.FileName;
                }
            }
        }

        private void OpenOutputFolder(string outputFolder)
        {
            if (!Directory.Exists(outputFolder))
            {
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + outputFolder + "\"",
                UseShellExecute = true
            });
        }

        private void UpdateDotSizeHint()
        {
            int dotSize = (int)_dotSize.Value;
            if (dotSize < 20)
            {
                _dotSizeHint.Text = "Small dot · best for tight grids.";
            }
            else if (dotSize <= 35)
            {
                _dotSizeHint.Text = "Recommended (20–35 px) for most sheets.";
            }
            else
            {
                _dotSizeHint.Text = "Large dot · use only for bigger bubbles.";
            }
        }

        private void UpdateSaqFontHint()
        {
            int size = (int)_saqFontSize.Value;
            _saqFontHint.Text = size == 0
                ? "0 = auto-size from the detected grid step (about 22 px at 300 dpi)."
                : "SAQ text drawn at " + size + " px.";
        }

        /// <summary>
        /// Live count under the roll / registration boxes. They are paired by
        /// position, so unequal lists mean the shorter one runs out partway
        /// through the batch - worth flagging before the run, not after.
        /// </summary>
        private void UpdateWriteOffsetHint()
        {
            if (_writeOffsetHint == null) return;

            if (!_writeStudentNumbers.Checked)
            {
                _writeOffsetHint.ForeColor = Theme.TextFaint;
                _writeOffsetHint.Text = "Turn on Write numbers to use this.";
                return;
            }

            decimal rows = _writeOffsetRows.Value;
            // StepY is one grid row and is about 36 px on a 300 dpi sheet.
            int approxPixels = (int)Math.Round(rows * 36m);
            _writeOffsetHint.ForeColor = Theme.TextFaint;
            _writeOffsetHint.Text = "Grid rows above the first bubble row (about " + approxPixels
                + " px at 300 dpi). 1.0 = the printed box row.";
        }

        private void UpdateStudentCountHint()
        {
            if (_studentCountHint == null) return;

            int rolls = OmrProcessor.SplitValues(_rollNumbers.Text).Length;
            int regs = OmrProcessor.SplitValues(_registrationNumbers.Text).Length;

            if (rolls == 0 && regs == 0)
            {
                _studentCountHint.ForeColor = Theme.TextFaint;
                _studentCountHint.Text = "No roll or registration numbers yet.";
                return;
            }

            string summary = Describe(rolls, "roll number") + "  ·  " + Describe(regs, "registration");

            if (rolls > 0 && regs > 0 && rolls != regs)
            {
                _studentCountHint.ForeColor = Theme.Warning;
                _studentCountHint.Text = summary + "  —  counts differ; the shorter list runs out first.";
                return;
            }

            _studentCountHint.ForeColor = Theme.TextFaint;
            _studentCountHint.Text = summary;
        }

        private static string Describe(int count, string noun)
        {
            if (count == 0) return "no " + noun + "s";
            return count + " " + noun + (count == 1 ? string.Empty : "s");
        }

        private void UpdateAnswerKeyHint()
        {
            List<string> problems;
            Dictionary<int, string[]> key = OmrProcessor.ParseAnswerKey(_answerKeyText.Text, out problems);
            string rest = _unlistedAnswers.SelectedIndex == 0 ? "filled randomly" : "left blank";

            if (problems.Count > 0)
            {
                _answerKeyHint.ForeColor = Theme.Danger;
                _answerKeyHint.Text = problems[0]
                    + (problems.Count > 1 ? "  (+" + (problems.Count - 1) + " more)" : string.Empty);
                return;
            }

            _answerKeyHint.ForeColor = Theme.TextFaint;

            if (key.Count == 0)
            {
                _answerKeyHint.Text = "No key yet - every MCQ question is " + rest
                    + ".   Format:  1-A,2-AC,3-B,4-ABCD";
                return;
            }

            int answered = key.Values.Count(answers => answers.Length > 0);
            int blanked = key.Count - answered;
            _answerKeyHint.Text = answered + " question(s) answered"
                + (blanked > 0 ? ", " + blanked + " left blank" : string.Empty)
                + " · every other question is " + rest + ".";
        }

        private void UpdateInputModeUi()
        {
            OmrInputMode mode = SelectedInputMode;

            if (mode == OmrInputMode.Pdf)
            {
                _inputLabel.Text = "PDF file";
                _inputBrowse.Text = "Browse";
                _toolTip.SetToolTip(_inputPath, PdfPageSource.IsAvailable
                    ? "Every page is rendered at 300 dpi; pages that are not this OMR sheet are skipped."
                    : "This copy of Windows does not provide the built-in PDF engine.");
                return;
            }

            _inputLabel.Text = mode == OmrInputMode.Folder ? "Input folder" : "Input file";
            _inputBrowse.Text = mode == OmrInputMode.Folder ? "Pick folder" : "Browse";
            _toolTip.SetToolTip(_inputPath, "Drag and drop the image or folder here.");
        }

        private string ResolveInputDrop(string[] paths)
        {
            if (paths == null || paths.Length == 0) return null;
            string first = paths[0];
            OmrInputMode mode = SelectedInputMode;

            if (mode == OmrInputMode.Pdf)
            {
                if (!File.Exists(first)) return null;
                return Path.GetExtension(first).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? first : null;
            }

            if (mode == OmrInputMode.SingleImage)
            {
                if (!File.Exists(first)) return null;
                return SupportedImageExtensions.Contains(Path.GetExtension(first), StringComparer.OrdinalIgnoreCase) ? first : null;
            }

            return Directory.Exists(first) ? first : null;
        }

        private static string ResolveOutputDrop(string[] paths)
        {
            if (paths == null || paths.Length == 0) return null;
            return Directory.Exists(paths[0]) ? paths[0] : null;
        }

        private static string ResolveXmlDrop(string[] paths)
        {
            if (paths == null || paths.Length == 0) return null;
            string first = paths[0];
            if (!File.Exists(first)) return null;
            return Path.GetExtension(first).Equals(".xml", StringComparison.OrdinalIgnoreCase) ? first : null;
        }
    }
}
