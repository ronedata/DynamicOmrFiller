using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DynamicOmrFiller
{
    /// <summary>
    /// Central colour, font and glyph palette for the application shell.
    /// Every custom control below paints itself from these values, so the whole
    /// look can be retuned from one place.
    /// </summary>
    internal static class Theme
    {
        // Surfaces
        public static readonly Color PageBack = Color.FromArgb(243, 245, 249);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceMuted = Color.FromArgb(237, 241, 246);
        public static readonly Color Border = Color.FromArgb(226, 232, 240);
        public static readonly Color BorderStrong = Color.FromArgb(203, 213, 225);

        // Text
        public static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
        public static readonly Color TextBody = Color.FromArgb(51, 65, 85);
        public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
        public static readonly Color TextFaint = Color.FromArgb(148, 163, 184);

        // Accent + state
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentPressed = Color.FromArgb(30, 64, 175);
        public static readonly Color AccentSoft = Color.FromArgb(235, 242, 254);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DisabledBack = Color.FromArgb(203, 213, 225);

        // Fonts
        public static readonly Font AppTitleFont = new Font("Segoe UI Semibold", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font CardTitleFont = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font BodyFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font InputFont = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font SmallFont = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font ButtonFont = new Font("Segoe UI Semibold", 9.25F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font StatusFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        private const string IconFamily = "Segoe MDL2 Assets";
        public static readonly Font IconFontLarge = new Font(IconFamily, 16F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font IconFont = new Font(IconFamily, 11.5F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font IconFontSmall = new Font(IconFamily, 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        // Glyphs (Segoe MDL2 Assets private-use code points)
        public const string GlyphFolder = "";
        public const string GlyphContact = "";
        public const string GlyphDocument = "";
        public const string GlyphSettings = "";
        public const string GlyphPlay = "";
        public const string GlyphOpenFile = "";
        public const string GlyphGrid = "";
        public const string GlyphSave = "";
        public const string GlyphChecklist = "";
        public const string GlyphChevronUp = "";
        public const string GlyphChevronDown = "";

        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0f || bounds.Width <= 0f || bounds.Height <= 0f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>First non-transparent background colour up the parent chain.</summary>
        public static Color ParentBack(Control control)
        {
            Control parent = control.Parent;
            while (parent != null)
            {
                if (parent.BackColor != Color.Transparent) return parent.BackColor;
                parent = parent.Parent;
            }
            return PageBack;
        }
    }

    /// <summary>Panel that can draw a 1px rule along its top or bottom edge.</summary>
    internal sealed class EdgePanel : Panel
    {
        public Color TopBorder { get; set; }
        public Color BottomBorder { get; set; }

        public EdgePanel()
        {
            TopBorder = Color.Empty;
            BottomBorder = Color.Empty;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (TopBorder != Color.Empty)
            {
                using (Pen pen = new Pen(TopBorder))
                {
                    e.Graphics.DrawLine(pen, 0, 0, Width, 0);
                }
            }
            if (BottomBorder != Color.Empty)
            {
                using (Pen pen = new Pen(BottomBorder))
                {
                    e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
                }
            }
        }
    }

    /// <summary>
    /// White rounded surface with an icon, a title and an optional subtitle.
    /// The whole header acts as a collapse / expand toggle for the card body.
    /// </summary>
    internal sealed class CardPanel : Panel
    {
        private readonly TableLayoutPanel _content;
        private readonly Label _chevron;
        private int _row;
        private bool _collapsed;

        /// <summary>Raised after the card has been collapsed or expanded.</summary>
        public event EventHandler CollapsedChanged;

        public CardPanel(string glyph, string title, string subtitle)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(20, 14, 20, 15);
            Margin = new Padding(0, 0, 0, 12);

            TableLayoutPanel stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Surface,
                Margin = new Padding(0)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(stack);

            _chevron = new Label
            {
                Text = Theme.GlyphChevronUp,
                Font = Theme.IconFontSmall,
                ForeColor = Theme.TextFaint,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0)
            };

            stack.Controls.Add(BuildHeader(glyph, title, subtitle), 0, 0);

            _content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 0,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Surface,
                Margin = new Padding(0)
            };
            _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            stack.Controls.Add(_content, 0, 1);

            Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = Theme.Border,
                Margin = new Padding(0, 11, 0, 11)
            });
        }

        /// <summary>
        /// Hides or shows everything under the header. Set it after the card's
        /// content has been added so the inner controls lay out once first.
        /// </summary>
        public bool Collapsed
        {
            get { return _collapsed; }
            set
            {
                if (_collapsed == value) return;
                _collapsed = value;
                _content.Visible = !value;
                _chevron.Text = value ? Theme.GlyphChevronDown : Theme.GlyphChevronUp;
                PerformLayout();
                Invalidate();

                EventHandler handler = CollapsedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        /// <summary>Appends a control as a new full-width row. Children should dock Top.</summary>
        public void Add(Control control)
        {
            _content.RowCount = _row + 1;
            _content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _content.Controls.Add(control, 0, _row);
            _row++;
        }

        /// <summary>Field grid pre-set with the card surface colour.</summary>
        public static TableLayoutPanel MakeGrid(int columns)
        {
            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = columns,
                RowCount = 0,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Surface,
                Margin = new Padding(0)
            };
            return grid;
        }

        private Control BuildHeader(string glyph, string title, string subtitle)
        {
            bool hasSubtitle = !string.IsNullOrEmpty(subtitle);

            TableLayoutPanel head = MakeGrid(3);
            head.RowCount = hasSubtitle ? 2 : 1;
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26F));
            head.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (hasSubtitle) head.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label icon = new Label
            {
                Text = glyph,
                Font = Theme.IconFont,
                ForeColor = Theme.Accent,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 8, 0)
            };
            head.Controls.Add(icon, 0, 0);
            if (hasSubtitle) head.SetRowSpan(icon, 2);

            Label titleLabel = new Label
            {
                Text = title,
                Font = Theme.CardTitleFont,
                ForeColor = Theme.TextPrimary,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 0)
            };
            head.Controls.Add(titleLabel, 1, 0);

            head.Controls.Add(_chevron, 2, 0);
            if (hasSubtitle) head.SetRowSpan(_chevron, 2);

            Label subtitleLabel = null;
            if (hasSubtitle)
            {
                subtitleLabel = new Label
                {
                    Text = subtitle,
                    Font = Theme.SmallFont,
                    ForeColor = Theme.TextMuted,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 3, 0, 0)
                };
                head.Controls.Add(subtitleLabel, 1, 1);
            }

            // The whole header row is the collapse target, not just the chevron.
            MakeToggleZone(head);
            MakeToggleZone(icon);
            MakeToggleZone(titleLabel);
            MakeToggleZone(_chevron);
            if (subtitleLabel != null) MakeToggleZone(subtitleLabel);

            return head;
        }

        private void MakeToggleZone(Control control)
        {
            control.Cursor = Cursors.Hand;
            control.Click += delegate { Collapsed = !Collapsed; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundedRect(bounds, 10f))
            using (SolidBrush fill = new SolidBrush(Theme.Surface))
            using (Pen pen = new Pen(Theme.Border, 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            base.OnPaint(e);
        }
    }

    /// <summary>Rounded, focus-aware container that hosts a borderless input control.</summary>
    internal sealed class InputHost : Panel
    {
        private readonly Control _inner;
        private readonly bool _multiline;
        private bool _focused;
        private bool _centering;

        public InputHost(Control inner, int height, bool multiline)
        {
            _inner = inner;
            _multiline = multiline;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Dock = DockStyle.Top;
            Height = height;
            Margin = new Padding(0, 4, 10, 4);
            Padding = new Padding(11, multiline ? 8 : 0, 11, multiline ? 8 : 0);

            inner.Dock = multiline ? DockStyle.Fill : DockStyle.Top;
            inner.BackColor = Theme.Surface;
            inner.ForeColor = Theme.TextPrimary;
            inner.Enter += InnerFocusChanged;
            inner.Leave += InnerFocusChanged;
            inner.GotFocus += InnerFocusChanged;
            inner.LostFocus += InnerFocusChanged;
            inner.SizeChanged += delegate { CenterInner(); };
            Controls.Add(inner);
            CenterInner();
        }

        public Control Inner
        {
            get { return _inner; }
        }

        private void InnerFocusChanged(object sender, EventArgs e)
        {
            _focused = _inner.Focused || _inner.ContainsFocus;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            CenterInner();
        }

        private void CenterInner()
        {
            if (_multiline || _centering) return;
            _centering = true;
            try
            {
                int top = Math.Max(0, (ClientSize.Height - _inner.Height) / 2);
                Padding = new Padding(11, top, 11, 0);
            }
            finally
            {
                _centering = false;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            RectangleF bounds = new RectangleF(0.8f, 0.8f, Width - 2.1f, Height - 2.1f);
            using (GraphicsPath path = Theme.RoundedRect(bounds, 6f))
            using (SolidBrush fill = new SolidBrush(Enabled ? Theme.Surface : Theme.PageBack))
            using (Pen pen = new Pen(_focused ? Theme.Accent : Theme.BorderStrong, _focused ? 1.6f : 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            base.OnPaint(e);
        }
    }

    /// <summary>Flat button with antialiased rounded corners, hover/press states and an optional glyph.</summary>
    internal class RoundedButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public Color BackNormal { get; set; }
        public Color BackHover { get; set; }
        public Color BackPressed { get; set; }
        public Color BorderNormal { get; set; }
        public Color TextNormal { get; set; }
        public Color ClearOverride { get; set; }
        public int Radius { get; set; }
        public string Glyph { get; set; }

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Font = Theme.ButtonFont;
            Cursor = Cursors.Hand;

            BackNormal = Theme.Accent;
            BackHover = Theme.AccentHover;
            BackPressed = Theme.AccentPressed;
            BorderNormal = Color.Transparent;
            TextNormal = Color.White;
            ClearOverride = Color.Empty;
            Radius = 7;
        }

        public static RoundedButton Primary(string text, string glyph, int width, int height)
        {
            return new RoundedButton
            {
                Text = text,
                Glyph = glyph,
                Size = new Size(width, height),
                Radius = 8
            };
        }

        public static RoundedButton Secondary(string text, int width, int height)
        {
            return new RoundedButton
            {
                Text = text,
                Size = new Size(width, height),
                BackNormal = Theme.Surface,
                BackHover = Color.FromArgb(241, 245, 249),
                BackPressed = Color.FromArgb(226, 232, 240),
                BorderNormal = Theme.BorderStrong,
                TextNormal = Theme.TextBody,
                Radius = 6
            };
        }

        public static RoundedButton Soft(string text, string glyph, int width, int height)
        {
            return new RoundedButton
            {
                Text = text,
                Glyph = glyph,
                Size = new Size(width, height),
                BackNormal = Theme.AccentSoft,
                BackHover = Color.FromArgb(222, 234, 253),
                BackPressed = Color.FromArgb(206, 224, 252),
                BorderNormal = Color.FromArgb(191, 214, 250),
                TextNormal = Theme.Accent,
                Radius = 6
            };
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ClearOverride != Color.Empty ? ClearOverride : Theme.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color back = BackNormal;
            Color fore = TextNormal;
            Color border = BorderNormal;

            if (!Enabled)
            {
                back = BackNormal == Theme.Surface ? Theme.PageBack : Theme.DisabledBack;
                fore = Theme.TextFaint;
                border = BorderNormal == Color.Transparent ? Color.Transparent : Theme.Border;
            }
            else if (_pressed)
            {
                back = BackPressed;
            }
            else if (_hover)
            {
                back = BackHover;
            }

            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundedRect(bounds, Radius))
            using (SolidBrush fill = new SolidBrush(back))
            {
                g.FillPath(fill, path);
                if (border != Color.Transparent)
                {
                    using (Pen pen = new Pen(border, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            bool hasGlyph = !string.IsNullOrEmpty(Glyph);
            Size textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            Size glyphSize = hasGlyph ? TextRenderer.MeasureText(Glyph, Theme.IconFontSmall) : Size.Empty;
            int gap = hasGlyph ? 7 : 0;
            int totalWidth = glyphSize.Width + gap + textSize.Width;
            int x = Math.Max(4, (Width - totalWidth) / 2);

            if (hasGlyph)
            {
                TextRenderer.DrawText(g, Glyph, Theme.IconFontSmall,
                    new Point(x, (Height - glyphSize.Height) / 2), fore);
                x += glyphSize.Width + gap;
            }

            TextRenderer.DrawText(g, Text ?? string.Empty, Font,
                new Point(x, (Height - textSize.Height) / 2), fore);
        }
    }

    /// <summary>Multi-segment pill selector used for the input mode.</summary>
    internal sealed class SegmentedToggle : Panel
    {
        private readonly RoundedButton[] _buttons;
        private int _selectedIndex;

        public event EventHandler SelectedIndexChanged;

        public SegmentedToggle(string[] labels, int segmentWidth)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 34;
            Width = (labels.Length * segmentWidth) + 8;
            Margin = new Padding(0, 4, 0, 4);

            _buttons = new RoundedButton[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                RoundedButton button = new RoundedButton
                {
                    Text = labels[i],
                    Size = new Size(segmentWidth, Height - 8),
                    Location = new Point(4 + (i * segmentWidth), 4),
                    Radius = 6,
                    TabStop = false,
                    ClearOverride = Theme.SurfaceMuted
                };
                button.Click += delegate { SelectedIndex = index; };
                _buttons[i] = button;
                Controls.Add(button);
            }

            ApplySegmentStyles();
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                if (value < 0 || value >= _buttons.Length || value == _selectedIndex) return;
                _selectedIndex = value;
                ApplySegmentStyles();
                EventHandler handler = SelectedIndexChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private void ApplySegmentStyles()
        {
            for (int i = 0; i < _buttons.Length; i++)
            {
                RoundedButton button = _buttons[i];
                if (i == _selectedIndex)
                {
                    button.BackNormal = Theme.Accent;
                    button.BackHover = Theme.Accent;
                    button.BackPressed = Theme.AccentPressed;
                    button.TextNormal = Color.White;
                }
                else
                {
                    button.BackNormal = Theme.SurfaceMuted;
                    button.BackHover = Color.FromArgb(226, 232, 240);
                    button.BackPressed = Theme.BorderStrong;
                    button.TextNormal = Theme.TextMuted;
                }
                button.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundedRect(bounds, 8f))
            using (SolidBrush fill = new SolidBrush(Theme.SurfaceMuted))
            using (Pen pen = new Pen(Theme.Border, 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            base.OnPaint(e);
        }
    }

    /// <summary>Pill-style on/off switch that keeps full CheckBox semantics.</summary>
    internal sealed class ToggleSwitch : CheckBox
    {
        private const int TrackWidth = 40;
        private const int TrackHeight = 21;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            AutoSize = false;
            Font = Theme.BodyFont;
            ForeColor = Theme.TextBody;
            Cursor = Cursors.Hand;
            Size = new Size(240, 26);
        }

        private void AutoFit()
        {
            Size textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            Width = TrackWidth + 11 + textSize.Width + 4;
            Height = Math.Max(26, textSize.Height + 8);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AutoFit();
            Invalidate();
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int trackTop = (Height - TrackHeight) / 2;
            RectangleF track = new RectangleF(0, trackTop, TrackWidth, TrackHeight);
            Color trackColor = Checked && Enabled ? Theme.Accent : Theme.BorderStrong;

            using (GraphicsPath path = Theme.RoundedRect(track, TrackHeight / 2f))
            using (SolidBrush fill = new SolidBrush(trackColor))
            {
                g.FillPath(fill, path);
            }

            float knob = TrackHeight - 4f;
            float knobX = Checked ? TrackWidth - knob - 2f : 2f;
            using (SolidBrush knobBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(knobBrush, knobX, trackTop + 2f, knob, knob);
            }

            Size textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            TextRenderer.DrawText(g, Text ?? string.Empty, Font,
                new Point(TrackWidth + 11, (Height - textSize.Height) / 2),
                Enabled ? ForeColor : Theme.TextFaint);
        }
    }

    /// <summary>Slim indeterminate progress strip shown while a batch runs.</summary>
    internal sealed class SlimProgress : Control
    {
        private readonly Timer _timer;
        private float _position;
        private bool _running;

        public SlimProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 3;
            _timer = new Timer { Interval = 16 };
            _timer.Tick += delegate
            {
                _position += 0.011f;
                if (_position > 1.3f) _position = -0.3f;
                Invalidate();
            };
        }

        public void Start()
        {
            _running = true;
            _position = -0.3f;
            _timer.Start();
            Invalidate();
        }

        public void Stop()
        {
            _running = false;
            _timer.Stop();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (!_running)
            {
                g.Clear(Theme.ParentBack(this));
                return;
            }

            g.Clear(Theme.AccentSoft);
            float barWidth = Width * 0.3f;
            using (SolidBrush brush = new SolidBrush(Theme.Accent))
            {
                g.FillRectangle(brush, _position * Width, 0, barWidth, Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Small coloured status dot next to the status text.</summary>
    internal sealed class StatusDot : Control
    {
        private Color _dotColor;

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(10, 10);
            _dotColor = Theme.Success;
        }

        public Color DotColor
        {
            get { return _dotColor; }
            set { _dotColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(_dotColor))
            {
                g.FillEllipse(brush, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            }
        }
    }

    /// <summary>Rounded badge label, used for the version chip.</summary>
    internal sealed class PillLabel : Label
    {
        public Color PillBack { get; set; }
        public Color PillBorder { get; set; }

        public PillLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = false;
            BackColor = Color.Transparent;
            Font = Theme.SmallFont;
            ForeColor = Theme.Accent;
            PillBack = Theme.AccentSoft;
            PillBorder = Color.FromArgb(199, 219, 252);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundedRect(bounds, Height / 2f))
            using (SolidBrush fill = new SolidBrush(PillBack))
            using (Pen pen = new Pen(PillBorder, 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            Size textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            TextRenderer.DrawText(g, Text ?? string.Empty, Font,
                new Point((Width - textSize.Width) / 2, (Height - textSize.Height) / 2), ForeColor);
        }
    }

    /// <summary>Rounded app-icon tile shown in the window header.</summary>
    internal sealed class IconTile : Control
    {
        private readonly Image _image;

        public IconTile(Image image)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(38, 38);
            _image = image;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundedRect(bounds, 9f))
            using (SolidBrush fill = new SolidBrush(Theme.AccentSoft))
            using (Pen pen = new Pen(Color.FromArgb(199, 219, 252), 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            if (_image != null)
            {
                int size = Math.Min(Width, Height) - 14;
                g.DrawImage(_image, (Width - size) / 2, (Height - size) / 2, size, size);
                return;
            }

            Size glyphSize = TextRenderer.MeasureText(Theme.GlyphGrid, Theme.IconFontLarge);
            TextRenderer.DrawText(g, Theme.GlyphGrid, Theme.IconFontLarge,
                new Point((Width - glyphSize.Width) / 2, (Height - glyphSize.Height) / 2), Theme.Accent);
        }
    }
}
