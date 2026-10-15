using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DynamicOmrFiller
{
    public class OmrProcessor
    {
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        /// <summary>A4 at 300 dpi - the resolution the marker detection is tuned for.</summary>
        private const int PdfRenderWidth = 2480;

        /// <summary>One sheet to fill: either an image file, or one page of a PDF.</summary>
        private sealed class PageSource
        {
            public string Label;        // shown in the log
            public string ImagePath;    // null for a PDF page
            public string OutputName;   // file name stem for the output
            public string Extension;    // output extension
            public PdfPageSource Pdf;   // shared document, null for an image
            public int PdfPageIndex;
        }
        private readonly Random _random = new Random();
        private int _dotSizePixels;
        private int _saqFontSizePixels;
        private int _saqLabelFontSizePixels;
        private StringAlignment _saqAlignHorizontal = StringAlignment.Center;
        private StringAlignment _saqAlignVertical = StringAlignment.Center;
        private OmrInputMode _inputMode;
        private bool _fillQuestionQuality;
        private bool _writeStudentNumbers = true;
        private bool _cropPdfToSheet = true;
        private float _writeOffsetRows = 1.0f;

        /// <summary>
        /// How many grid rows tall the printed write-in boxes are. Measured off
        /// the College Admission sheet; used for the digit box and font size,
        /// while WriteOffsetRows decides how far up the box sits.
        /// </summary>
        private const float WriteBandCells = 2f;
        private bool _exportAnswerKey;
        private Dictionary<int, string[]> _answerKey = new Dictionary<int, string[]>();
        private bool _unlistedRandom = true;
        private int _questionCursor;
        private string _firstPageId;
        private readonly List<AnswerKeyEntry> _answerLog = new List<AnswerKeyEntry>();
        private string _currentOutputName;
        private string _currentPageId;
        private string _currentRollLabel;
        private string _currentRegLabel;

        public event EventHandler<OmrLogEventArgs> LogMessage;

        public OmrBatchResult ProcessBatch(OmrBatchOptions options)
        {
            ValidateOptions(options);

            string[] rollList = SplitValues(options.RollNumbersInput);
            string[] regList = SplitValues(options.RegistrationNumbersInput);
            _inputMode = options.InputMode;
            _dotSizePixels = options.DotSizePixels;
            _saqFontSizePixels = options.SaqFontSizePixels;
            _saqLabelFontSizePixels = options.SaqLabelFontSizePixels;
            _saqAlignHorizontal = options.SaqAlignHorizontal;
            _saqAlignVertical = options.SaqAlignVertical;
            _fillQuestionQuality = options.FillQuestionQuality;
            _writeStudentNumbers = options.WriteStudentNumbers;
            _cropPdfToSheet = options.CropPdfToSheet;
            _writeOffsetRows = options.WriteOffsetRows;
            _exportAnswerKey = options.ExportAnswerKey;
            _answerKey = ParseAnswerKey(options.AnswerKeyInput);
            _unlistedRandom = options.UnlistedAnswersRandom;
            _questionCursor = 0;
            _firstPageId = FirstPageId(LoadOmrInfoRegion(options.XmlPath));
            _answerLog.Clear();

            options.OutputFolder = ResolveOutputFolder(options);
            Directory.CreateDirectory(options.OutputFolder);

            int studentIndex = 0;
            string currentRoll = rollList.Length > 0 ? rollList[0] : string.Empty;
            string currentReg = regList.Length > 0 ? regList[0] : string.Empty;

            List<PdfPageSource> openPdfs = new List<PdfPageSource>();
            List<PageSource> sources;
            try
            {
                sources = ResolvePageSources(options.InputPath, openPdfs);
            }
            catch
            {
                foreach (PdfPageSource pdf in openPdfs) pdf.Dispose();
                throw;
            }

            OmrBatchResult result = new OmrBatchResult { TotalImages = sources.Count };
            Log("Processing started. Found " + sources.Count
                + (_inputMode == OmrInputMode.Pdf ? " PDF page(s)." : " image(s)."));
            int keyAnswered = _answerKey.Values.Count(answers => answers.Length > 0);
            Log("MCQ answer key: " + keyAnswered + " answered, " + (_answerKey.Count - keyAnswered)
                + " explicitly blank; every other question is "
                + (_unlistedRandom ? "filled randomly." : "left blank."));

            try
            {
                foreach (PageSource source in sources)
                {
                    string outputFilePath = BuildOutputPath(options.OutputFolder, source);
                    try
                    {
                        using (Bitmap original = LoadSource(source))
                        {
                            ProcessOmrImage(original, source.Label, outputFilePath, options.XmlPath,
                                rollList, regList, ref studentIndex, ref currentRoll, ref currentReg,
                                options.SaqExtraText);
                        }
                        result.SuccessCount++;
                        result.OutputFiles.Add(outputFilePath);
                        Log("Done: " + source.Label);
                    }
                    catch (Exception ex)
                    {
                        // A PDF normally carries question pages alongside the
                        // sheets, so a page that is not this OMR design is
                        // skipped rather than counted as a failure.
                        if (source.Pdf != null)
                        {
                            result.SkippedCount++;
                            Log("Skipped: " + source.Label + " - " + ex.Message);
                        }
                        else
                        {
                            result.FailureCount++;
                            Log("Failed: " + source.Label + " - " + ex.Message);
                        }
                    }
                }
            }
            finally
            {
                foreach (PdfPageSource pdf in openPdfs) pdf.Dispose();
            }

            if (_exportAnswerKey && _answerLog.Count > 0)
            {
                try
                {
                    result.AnswerKeyFile = WriteAnswerKeyCsv(options.OutputFolder);
                    Log("Answer key exported: " + Path.GetFileName(result.AnswerKeyFile) + " (" + _answerLog.Count + " row(s))");
                }
                catch (Exception ex)
                {
                    Log("Could not write the answer key file - " + ex.Message);
                }
            }

            Log("Completed. Success: " + result.SuccessCount + ", Failed: " + result.FailureCount
                + (result.SkippedCount > 0 ? ", Skipped: " + result.SkippedCount : string.Empty));
            return result;
        }

        private void ValidateOptions(OmrBatchOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.InputPath)) throw new InvalidOperationException("Input path is required.");
            if (string.IsNullOrWhiteSpace(options.XmlPath)) throw new InvalidOperationException("XML path is required.");
            if (!File.Exists(options.XmlPath)) throw new FileNotFoundException("XML file not found.", options.XmlPath);
        }

        private string ResolveOutputFolder(OmrBatchOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.OutputFolder)) return options.OutputFolder;

            string baseDir;
            if (Directory.Exists(options.InputPath))
            {
                baseDir = options.InputPath;
            }
            else if (File.Exists(options.InputPath))
            {
                baseDir = Path.GetDirectoryName(options.InputPath);
            }
            else
            {
                throw new InvalidOperationException("Cannot derive output folder — input path is invalid.");
            }

            return Path.Combine(baseDir ?? string.Empty, "FillUpOMR");
        }

        /// <summary>
        /// Expands the chosen input into the individual sheets to fill. PDFs
        /// contribute one source per page; any PdfPageSource created here is
        /// added to <paramref name="openPdfs"/> for the caller to dispose.
        /// </summary>
        private List<PageSource> ResolvePageSources(string inputPath, List<PdfPageSource> openPdfs)
        {
            List<PageSource> sources = new List<PageSource>();

            if (_inputMode == OmrInputMode.Pdf)
            {
                if (!File.Exists(inputPath)) throw new FileNotFoundException("Selected PDF was not found.", inputPath);
                if (!PdfPageSource.IsPdf(inputPath)) throw new InvalidOperationException("Input file must be a .pdf.");

                PdfPageSource pdf = new PdfPageSource(inputPath);
                openPdfs.Add(pdf);

                string stem = Path.GetFileNameWithoutExtension(inputPath);
                for (int page = 0; page < pdf.PageCount; page++)
                {
                    sources.Add(new PageSource
                    {
                        Label = stem + " page " + (page + 1),
                        OutputName = stem + "_p" + (page + 1),
                        Extension = ".jpg",
                        Pdf = pdf,
                        PdfPageIndex = page
                    });
                }
                return sources;
            }

            if (_inputMode == OmrInputMode.SingleImage)
            {
                if (!File.Exists(inputPath)) throw new FileNotFoundException("Selected input file was not found.", inputPath);
                if (!SupportedExtensions.Contains(Path.GetExtension(inputPath), StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Input file must be an image.");
                }
                sources.Add(ImageSource(inputPath));
                return sources;
            }

            if (Directory.Exists(inputPath))
            {
                foreach (string path in Directory.GetFiles(inputPath, "*.*")
                    .Where(p => SupportedExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    sources.Add(ImageSource(path));
                }
                return sources;
            }

            if (File.Exists(inputPath))
            {
                if (!SupportedExtensions.Contains(Path.GetExtension(inputPath), StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Input file must be an image.");
                }
                sources.Add(ImageSource(inputPath));
                return sources;
            }

            throw new DirectoryNotFoundException("Input path not found.");
        }

        private static PageSource ImageSource(string path)
        {
            return new PageSource
            {
                Label = Path.GetFileName(path),
                ImagePath = path,
                OutputName = Path.GetFileNameWithoutExtension(path),
                Extension = Path.GetExtension(path)
            };
        }

        private Bitmap LoadSource(PageSource source)
        {
            if (source.Pdf != null) return source.Pdf.RenderPage(source.PdfPageIndex, PdfRenderWidth);
            return new Bitmap(source.ImagePath);
        }

        /// <summary>
        /// Splits a roll / registration list. The UI offers a text area, so the
        /// values may be separated by newlines as well as commas.
        /// </summary>
        public static string[] SplitValues(string input)
        {
            return (input ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .ToArray();
        }

        private static string BuildOutputPath(string outputFolder, PageSource source)
        {
            string timestamp = DateTime.Now.ToString("HHmmss_fff", CultureInfo.InvariantCulture);
            return Path.Combine(outputFolder, source.OutputName + "_edit_" + timestamp + source.Extension);
        }

        private void ProcessOmrImage(Bitmap originalBmp, string label, string outputPath, string xmlPath, string[] rollList, string[] regList, ref int studentIndex, ref string currentRoll, ref string currentReg, string saqExtra)
        {
            Log("Processing: " + label);

            using (Bitmap workBmp = new Bitmap(originalBmp))
            {
                workBmp.SetResolution(originalBmp.HorizontalResolution, originalBmp.VerticalResolution);

                GridSystemData gridData;

                using (Graphics g = Graphics.FromImage(workBmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    gridData = DetectAndCalculateGrid(workBmp);
                    if (gridData == null)
                    {
                        throw new InvalidOperationException("Grid detection failed.");
                    }

                    Log("  Grid: " + gridData.XLines.Count + " x " + gridData.YLines.Count
                        + " cells, step " + gridData.StepX.ToString("0.0", CultureInfo.InvariantCulture) + "px");

                    // The XML states the grid it was drawn for; a page that
                    // cannot supply it is not this sheet (a question page, say),
                    // and drawing on it would silently produce nonsense.
                    int designX, designY;
                    LoadDesignGrid(xmlPath, out designX, out designY);
                    // Detection normally reports one cell more than the design
                    // asks for, so require the full count: a sheet that lands
                    // one short is a different layout that happens to have a
                    // similar grid, not this one.
                    if ((designX > 0 && gridData.XLines.Count < designX) ||
                        (designY > 0 && gridData.YLines.Count < designY))
                    {
                        throw new InvalidOperationException(
                            "The detected grid is " + gridData.XLines.Count + "x" + gridData.YLines.Count
                            + " cells but the XML needs " + designX + "x" + designY
                            + ". This page does not look like that OMR sheet.");
                    }

                    OmrRegion omrNoRegion = LoadOmrInfoRegion(xmlPath);
                    string detectedPageId = DetectPageIdFromImage(workBmp, gridData, omrNoRegion);
                    if (detectedPageId == null)
                    {
                        throw new InvalidOperationException(
                            "The page-number bubble could not be read (charSet '"
                            + (omrNoRegion == null ? string.Empty : omrNoRegion.CharSet)
                            + "'), so this page cannot be matched to a section of the XML.");
                    }
                    Log("  Detected Page ID: " + detectedPageId);

                    _currentOutputName = Path.GetFileName(outputPath);
                    _currentPageId = detectedPageId;

                    // A single-page or virtual design has no "page 1" bubble, so
                    // every sheet is a new student; a multi-page design starts one
                    // on its first page id.
                    if (_firstPageId == null || detectedPageId == _firstPageId)
                    {
                        // The answer key restarts at question 1 and then keeps
                        // running across that student's remaining pages.
                        _questionCursor = 0;

                        if (studentIndex < rollList.Length)
                        {
                            currentRoll = rollList[studentIndex];
                            currentReg = studentIndex < regList.Length ? regList[studentIndex] : string.Empty;
                            Log("  Assigned Roll: " + currentRoll);
                            studentIndex++;
                        }
                        else if (rollList.Length > 0)
                        {
                            Log("  No more roll numbers. Reusing the last available one.");
                        }
                    }
                    else
                    {
                        Log("  Continuing with Roll: " + currentRoll + " (Page " + detectedPageId + ")");
                    }

                    _currentRollLabel = currentRoll;
                    _currentRegLabel = currentReg;

                    List<OmrRegion> activeRegions = LoadRegionsForPage(xmlPath, detectedPageId);
                    if (activeRegions == null)
                    {
                        throw new InvalidOperationException(
                            "The XML has no <omr omrNo=\"" + detectedPageId + "\"> section. It defines: "
                            + string.Join(", ", AvailablePageIds(xmlPath)) + ".");
                    }

                    int handled = 0;
                    foreach (OmrRegion region in activeRegions)
                    {
                        if (region == null || string.IsNullOrWhiteSpace(region.MemberName))
                        {
                            continue;
                        }

                        if (region.StartX > gridData.XLines.Count || region.StartY > gridData.YLines.Count)
                        {
                            Log("  Region '" + region.MemberName + "' starts at (" + region.StartX + "," + region.StartY
                                + "), outside the detected " + gridData.XLines.Count + "x" + gridData.YLines.Count
                                + " grid - skipped.");
                            continue;
                        }

                        if (region.MemberName == "rollNo")
                        {
                            DrawMappedField(g, gridData, region, currentRoll);
                            if (_writeStudentNumbers) DrawWrittenNumber(g, gridData, region, currentRoll);
                            handled++;
                        }
                        else if (region.MemberName == "registrationNo")
                        {
                            DrawMappedField(g, gridData, region, currentReg);
                            if (_writeStudentNumbers) DrawWrittenNumber(g, gridData, region, currentReg);
                            handled++;
                        }
                        else if (region.MemberName.StartsWith("answer", StringComparison.OrdinalIgnoreCase))
                        {
                            DrawAnswers(g, gridData, region);
                            handled++;
                        }
                        else if (region.MemberName.StartsWith("SaqAnswer", StringComparison.OrdinalIgnoreCase))
                        {
                            DrawSaqMappingLabel(g, gridData, region, saqExtra);
                            handled++;
                        }
                        else if (string.Equals(region.MemberName, "QuestionQuality", StringComparison.OrdinalIgnoreCase) && _fillQuestionQuality)
                        {
                            DrawRandomQuestionQuality(g, gridData, region);
                            handled++;
                        }
                    }

                    Log("  Regions filled: " + handled + " of " + activeRegions.Count);
                    if (handled == 0)
                    {
                        throw new InvalidOperationException(
                            "Page " + detectedPageId + " matched " + activeRegions.Count
                            + " region(s) but none could be filled - nothing would be drawn.");
                    }
                }

                if (_inputMode == OmrInputMode.Pdf && _cropPdfToSheet)
                {
                    using (Bitmap trimmed = CropToSheet(workBmp, gridData))
                    {
                        if (trimmed != null)
                        {
                            Log("  Cropped to the sheet: " + trimmed.Width + " x " + trimmed.Height + "px");
                            SaveBitmap(trimmed, outputPath);
                            return;
                        }
                    }
                }

                SaveBitmap(workBmp, outputPath);
            }
        }

        /// <summary>
        /// Trims a filled page down to the OMR sheet, keeping a margin of two
        /// grid rows around the fiducial markers - which is roughly how the
        /// pre-cropped sheets are trimmed. Returns null when there is nothing
        /// worth trimming.
        /// </summary>
        private static Bitmap CropToSheet(Bitmap source, GridSystemData grid)
        {
            Rectangle sheet = grid.SheetBounds;
            if (sheet.Width <= 0 || sheet.Height <= 0) return null;

            int margin = Math.Max(4, (int)Math.Round(grid.StepY * 2f));
            Rectangle area = Rectangle.FromLTRB(
                Math.Max(0, sheet.Left - margin),
                Math.Max(0, sheet.Top - margin),
                Math.Min(source.Width, sheet.Right + margin),
                Math.Min(source.Height, sheet.Bottom + margin));

            if (area.Width <= 0 || area.Height <= 0) return null;
            if (area.Width >= source.Width && area.Height >= source.Height) return null;

            Bitmap cropped = new Bitmap(area.Width, area.Height);
            cropped.SetResolution(source.HorizontalResolution, source.VerticalResolution);
            using (Graphics g = Graphics.FromImage(cropped))
            {
                g.DrawImage(source, new Rectangle(0, 0, area.Width, area.Height), area, GraphicsUnit.Pixel);
            }
            return cropped;
        }

        private static void SaveBitmap(Bitmap bitmap, string outputPath)
        {
            string extension = Path.GetExtension(outputPath);
            ImageFormat format = ImageFormat.Jpeg;

            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                format = ImageFormat.Png;
            }
            else if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                format = ImageFormat.Bmp;
            }

            bitmap.Save(outputPath, format);
        }

        private void DrawSaqMappingLabel(Graphics g, GridSystemData grid, OmrRegion region, string extraText)
        {
            if (region.StartX > grid.XLines.Count || region.StartY > grid.YLines.Count) return;

            float originX = grid.XLines[region.StartX - 1];
            float originY = grid.YLines[region.StartY - 1];
            float startPixelX = originX + (region.PaddingX * grid.StepX);
            float startPixelY = originY + (region.PaddingY * grid.StepY);

            float extraFontSize = _saqFontSizePixels > 0
                ? _saqFontSizePixels
                : Math.Max(14f, Math.Min(26f, grid.StepY * 0.6f));
            float labelFontSize = _saqLabelFontSizePixels > 0
                ? _saqLabelFontSizePixels
                : Math.Max(10f, Math.Min(18f, grid.StepY * 0.4f));

            using (Font extraFont = new Font("Arial", extraFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font labelFont = new Font("Arial", labelFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(Color.Black))
            using (StringFormat extraFormat = new StringFormat { Alignment = _saqAlignHorizontal, LineAlignment = _saqAlignVertical, Trimming = StringTrimming.Word })
            using (StringFormat labelFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                if (region.ResultCountCellX > 0 && region.ResultCountCellY > 0)
                {
                    float totalWidth = region.ResultCountCellX * grid.StepX;
                    float totalHeight = region.ResultCountCellY * grid.StepY;
                    DrawSaqCell(g, extraText, region.MemberName, extraFont, labelFont, brush,
                        extraFormat, labelFormat, startPixelX, startPixelY, totalWidth, totalHeight);
                }
                else
                {
                    int count = region.Lines > 0 ? region.Lines : 1;
                    float availableWidth = grid.XLines.Last() - grid.XLines[region.StartX - 1];

                    for (int i = 0; i < count; i++)
                    {
                        float spacingFactorY = 1.0f + region.SpacingY;
                        float cellTopY = startPixelY + (i * grid.StepY * spacingFactorY);
                        float cellHeight = grid.StepY;
                        string label = count > 1 ? region.MemberName + " " + (i + 1) : region.MemberName;

                        if (!string.IsNullOrEmpty(extraText))
                        {
                            DrawSaqCell(g, extraText, label, extraFont, labelFont, brush,
                                extraFormat, labelFormat, startPixelX, cellTopY, availableWidth, cellHeight);
                        }
                        else
                        {
                            RectangleF cellRect = new RectangleF(startPixelX, cellTopY, availableWidth, cellHeight);
                            g.DrawString(label, labelFont, brush, cellRect, labelFormat);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Draws one SAQ cell. The auto-generated label keeps a strip along the
        /// bottom; the user's text is aligned anywhere in the space above it,
        /// so "move it up / down / left / right / centre" never collides with
        /// the label.
        /// </summary>
        private static void DrawSaqCell(Graphics g, string extraText, string label,
            Font extraFont, Font labelFont, Brush brush,
            StringFormat extraFormat, StringFormat labelFormat,
            float x, float y, float width, float height)
        {
            if (string.IsNullOrEmpty(extraText))
            {
                g.DrawString(label, labelFont, brush, new RectangleF(x, y, width, height), labelFormat);
                return;
            }

            float labelStrip = Math.Min(height * 0.4f, labelFont.GetHeight(g) * 1.5f);
            float insetX = Math.Min(12f, width * 0.01f);
            float insetY = Math.Min(10f, height * 0.06f);

            RectangleF textRect = new RectangleF(
                x + insetX,
                y + insetY,
                Math.Max(1f, width - (insetX * 2f)),
                Math.Max(1f, height - labelStrip - insetY));
            g.DrawString(extraText, extraFont, brush, textRect, extraFormat);

            RectangleF labelRect = new RectangleF(x, y + height - labelStrip, width, labelStrip);
            g.DrawString(label, labelFont, brush, labelRect, labelFormat);
        }

        /// <summary>
        /// Writes the digits of a mapped value into the boxes printed above the
        /// bubble grid - one digit per bubble column, so they line up with the
        /// dots below. The band is the two grid rows directly above the region
        /// origin, which is where these sheets print the write-in row.
        /// </summary>
        private void DrawWrittenNumber(Graphics g, GridSystemData grid, OmrRegion region, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (region.StartX > grid.XLines.Count || region.StartY > grid.YLines.Count) return;

            float originX = grid.XLines[region.StartX - 1];
            float originY = grid.YLines[region.StartY - 1];
            float bandHeight = grid.StepY * WriteBandCells;

            // The digits are centred WriteOffsetRows above the first bubble row,
            // so 1.0 lands in the printed box row and the user can nudge from there.
            float centreY = originY - (_writeOffsetRows * grid.StepY);
            float bandTop = centreY - (bandHeight / 2f);
            if (bandTop < 0f)
            {
                Log("  '" + region.MemberName + "' write offset of " + _writeOffsetRows
                    + " row(s) puts the digits above the top of the sheet - not written.");
                return;
            }

            int count = value.Length;
            if (region.Lines > 0 && count > region.Lines)
            {
                Log("  '" + region.MemberName + "' has " + region.Lines + " column(s) but the value is "
                    + value.Length + " character(s); the extra ones are not written.");
                count = region.Lines;
            }

            // Keep the digit inside its box on both axes.
            float fontSize = Math.Min(bandHeight * 0.62f, grid.StepX * 1.15f);

            using (Font font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(Color.Black))
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                for (int i = 0; i < count; i++)
                {
                    float cellX = originX + (region.PaddingX * grid.StepX)
                        + (i * grid.StepX * (1.0f + region.SpacingX));
                    g.DrawString(value[i].ToString(), font, brush,
                        new RectangleF(cellX, bandTop, grid.StepX, bandHeight), format);
                }
            }
        }

        private void DrawMappedField(Graphics g, GridSystemData grid, OmrRegion region, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (region.StartX > grid.XLines.Count || region.StartY > grid.YLines.Count) return;

            float originX = grid.XLines[region.StartX - 1];
            float originY = grid.YLines[region.StartY - 1];

            for (int i = 0; i < value.Length; i++)
            {
                char digit = value[i];
                int charIndex = (region.CharSet ?? string.Empty).IndexOf(digit);
                if (charIndex == -1) continue;

                float lineX = originX + (region.PaddingX * grid.StepX) + (i * grid.StepX * (1.0f + region.SpacingX));
                float lineY = originY + (region.PaddingY * grid.StepY) + (charIndex * grid.StepY * (1.0f + region.SpacingY));
                DrawDot(g, lineX, lineY, grid.StepX, grid.StepY);
            }
        }

        /// <summary>
        /// Fills the MCQ bubbles of an "answer*" region. When the user supplied
        /// an answer key those exact answers are used; otherwise each question
        /// gets a random choice from the region's charSet (the original
        /// behaviour). Only these regions consult the key - roll number,
        /// registration number and SAQ regions are untouched by it.
        /// </summary>
        private void DrawAnswers(Graphics g, GridSystemData grid, OmrRegion region)
        {
            if (string.IsNullOrEmpty(region.CharSet)) return;
            if (region.StartX > grid.XLines.Count || region.StartY > grid.YLines.Count) return;

            float originX = grid.XLines[region.StartX - 1];
            float originY = grid.YLines[region.StartY - 1];
            List<string> resultLog = new List<string>();

            for (int q = 0; q < region.Lines; q++)
            {
                // lineStart states the real question number of a region's first
                // row (this sheet has answer regions for 1-10 and 11-20), so the
                // key's numbers line up with the paper.
                int questionNumber;
                if (region.LineStart > 0)
                {
                    questionNumber = region.LineStart + q;
                    _questionCursor = questionNumber;
                }
                else
                {
                    _questionCursor++;
                    questionNumber = _questionCursor;
                }

                string[] wanted = ResolveAnswers(region, questionNumber);
                float lineY = originY + (region.PaddingY * grid.StepY) + (q * grid.StepY * (1.0f + region.SpacingY));
                StringBuilder drawn = new StringBuilder();

                // A question can carry more than one answer (e.g. "2-AC"), so
                // every listed option gets its own dot on the same row.
                foreach (string token in wanted)
                {
                    int ansIndex = IndexInCharSet(region.CharSet, token);
                    if (ansIndex < 0)
                    {
                        Log("  Answer '" + token + "' for question " + questionNumber + " is not in charSet '"
                            + region.CharSet + "' - skipped.");
                        continue;
                    }

                    float lineX = originX + (region.PaddingX * grid.StepX) + (ansIndex * grid.StepX * (1.0f + region.SpacingX));
                    DrawDot(g, lineX, lineY, grid.StepX, grid.StepY);
                    drawn.Append(OptionAt(region.CharSet, ansIndex));
                }

                if (drawn.Length == 0)
                {
                    resultLog.Add(questionNumber + "-(blank)");
                    continue;
                }

                resultLog.Add(questionNumber + "-" + drawn.ToString());
                Record(region.MemberName, questionNumber, drawn.ToString());
            }

            Log("  Answers (" + region.MemberName + "): " + string.Join(", ", resultLog));
        }

        /// <summary>
        /// The options to bubble for one question. An empty array means the
        /// question is left blank - either because the key blanked it
        /// explicitly, or because it is unlisted and the "other questions"
        /// setting is "leave blank".
        /// </summary>
        private string[] ResolveAnswers(OmrRegion region, int questionNumber)
        {
            string[] listed;
            if (_answerKey.TryGetValue(questionNumber, out listed))
            {
                return listed;
            }

            if (!_unlistedRandom) return new string[0];

            int count = OptionCount(region.CharSet);
            if (count == 0) return new string[0];
            return new[] { OptionAt(region.CharSet, _random.Next(0, count)) };
        }

        /// <summary>
        /// Parses the MCQ answer key. Supported forms, which may be mixed:
        ///
        ///   "1-A,2-AC,3-B,4-ABCD"   explicit question numbers; several letters
        ///                           bubble several options on that question
        ///   "1-A,2-AC,3-B,,,"       an empty slot blanks that question
        ///   "4-"                    also blanks question 4
        ///   "A,C,B,D"               positional - questions 1, 2, 3, 4
        ///   "ACBD"                  shorthand, one answer per question
        ///
        /// Returns question number to the options to bubble. An entry with an
        /// empty array is a deliberately blanked question; questions missing
        /// from the map are not covered by the key at all.
        /// </summary>
        public static Dictionary<int, string[]> ParseAnswerKey(string input)
        {
            List<string> ignored;
            return ParseAnswerKey(input, out ignored);
        }

        /// <summary>
        /// As above, and reports anything that looks like a typo so the UI can
        /// warn before a batch runs instead of quietly bubbling nonsense.
        /// </summary>
        public static Dictionary<int, string[]> ParseAnswerKey(string input, out List<string> problems)
        {
            problems = new List<string>();
            Dictionary<int, string[]> map = new Dictionary<int, string[]>();
            if (string.IsNullOrWhiteSpace(input)) return map;

            string trimmed = input.Trim();

            // No separator anywhere: treat each character as its own question.
            if (trimmed.IndexOfAny(new[] { ',', ';', '-', ' ', '\t', '\r', '\n' }) < 0)
            {
                for (int i = 0; i < trimmed.Length; i++)
                {
                    map[i + 1] = new[] { trimmed[i].ToString() };
                }
                return map;
            }

            string[] tokens = trimmed.Split(',', ';', '\n');
            int cursor = 0;

            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();

                if (token.Length == 0)
                {
                    // An empty slot blanks the next question in sequence.
                    cursor++;
                    map[cursor] = new string[0];
                    continue;
                }

                int dash = token.IndexOf('-');
                int number;
                string[] answers;

                if (dash > 0 && int.TryParse(token.Substring(0, dash).Trim(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number > 0)
                {
                    answers = SplitAnswers(token.Substring(dash + 1));
                    cursor = number;
                }
                else
                {
                    if (token.IndexOf('-') >= 0)
                    {
                        problems.Add("'" + token + "' - the part before the dash must be a question number, like 3-B.");
                    }

                    cursor++;
                    number = cursor;
                    answers = SplitAnswers(token);
                }

                if (map.ContainsKey(number))
                {
                    problems.Add("Question " + number + " is listed more than once; the last entry wins.");
                }

                foreach (string answer in answers)
                {
                    if (!IsPlainAnswer(answer))
                    {
                        problems.Add("Question " + number + ": '" + answer
                            + "' is not a plain answer - use letters or digits only.");
                    }
                }

                map[number] = answers;
            }

            return map;
        }

        private static bool IsPlainAnswer(string answer)
        {
            if (string.IsNullOrEmpty(answer)) return false;
            foreach (char ch in answer)
            {
                if (!char.IsLetterOrDigit(ch)) return false;
            }
            return true;
        }

        /// <summary>Splits one question's answers: "AC" -> A, C; "A/C" -> A, C.</summary>
        private static string[] SplitAnswers(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new string[0];

            string trimmed = value.Trim();
            char[] separators = { ' ', '\t', '/', '|', '+' };

            if (trimmed.IndexOfAny(separators) >= 0)
            {
                return trimmed.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToArray();
            }

            return trimmed.Select(ch => ch.ToString()).ToArray();
        }

        /// <summary>Number of selectable options in a charSet.</summary>
        private static int OptionCount(string charSet)
        {
            if (string.IsNullOrEmpty(charSet)) return 0;
            return charSet.Contains(",") ? ParseCharSet(charSet).Length : charSet.Length;
        }

        /// <summary>The option label at a given index of a charSet.</summary>
        private static string OptionAt(string charSet, int index)
        {
            if (string.IsNullOrEmpty(charSet) || index < 0) return string.Empty;

            if (charSet.Contains(","))
            {
                string[] options = ParseCharSet(charSet);
                return index < options.Length ? options[index] : string.Empty;
            }

            return index < charSet.Length ? charSet[index].ToString() : string.Empty;
        }

        /// <summary>Position of an answer token inside a region's charSet, case-insensitive.</summary>
        private static int IndexInCharSet(string charSet, string token)
        {
            if (string.IsNullOrEmpty(charSet) || string.IsNullOrEmpty(token)) return -1;

            if (charSet.Contains(","))
            {
                string[] options = ParseCharSet(charSet);
                for (int i = 0; i < options.Length; i++)
                {
                    if (string.Equals(options[i], token, StringComparison.OrdinalIgnoreCase)) return i;
                }
                return -1;
            }

            if (token.Length != 1) return -1;
            for (int i = 0; i < charSet.Length; i++)
            {
                if (char.ToUpperInvariant(charSet[i]) == char.ToUpperInvariant(token[0])) return i;
            }
            return -1;
        }

        private void Record(string regionName, int question, string answer)
        {
            if (!_exportAnswerKey) return;

            _answerLog.Add(new AnswerKeyEntry
            {
                OutputFile = _currentOutputName,
                Roll = _currentRollLabel,
                Registration = _currentRegLabel,
                PageId = _currentPageId,
                Region = regionName,
                Question = question,
                Answer = answer
            });
        }

        private string WriteAnswerKeyCsv(string outputFolder)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(outputFolder, "answer_key_" + stamp + ".csv");

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("OutputFile,Roll,Registration,PageId,Region,Question,Answer");
            foreach (AnswerKeyEntry entry in _answerLog)
            {
                builder.Append(CsvField(entry.OutputFile)).Append(',')
                    .Append(CsvField(entry.Roll)).Append(',')
                    .Append(CsvField(entry.Registration)).Append(',')
                    .Append(CsvField(entry.PageId)).Append(',')
                    .Append(CsvField(entry.Region)).Append(',')
                    .Append(entry.Question.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(CsvField(entry.Answer));
                builder.AppendLine();
            }

            // BOM so Excel opens the file as UTF-8 without mangling the values.
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string CsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private void DrawRandomQuestionQuality(Graphics g, GridSystemData grid, OmrRegion region)
        {
            if (string.IsNullOrEmpty(region.CharSet)) return;
            if (region.StartX > grid.XLines.Count || region.StartY > grid.YLines.Count) return;

            string[] options = ParseCharSet(region.CharSet);
            if (options.Length == 0) return;

            int idx = _random.Next(0, options.Length);

            float originX = grid.XLines[region.StartX - 1];
            float originY = grid.YLines[region.StartY - 1];
            float lineX = originX + (region.PaddingX * grid.StepX);
            float lineY = originY + (region.PaddingY * grid.StepY);

            if (string.Equals(region.Direction, "x", StringComparison.OrdinalIgnoreCase))
            {
                lineX += idx * grid.StepX * (1.0f + region.SpacingX);
            }
            else
            {
                lineY += idx * grid.StepY * (1.0f + region.SpacingY);
            }

            DrawDot(g, lineX, lineY, grid.StepX, grid.StepY);
            Record(region.MemberName, 1, options[idx]);
            Log("  QuestionQuality: " + options[idx]);
        }

        private void DrawDot(Graphics g, float x, float y, float stepX, float stepY)
        {
            float centerX = x + (stepX * 0.5f);
            float centerY = y + (stepY * 0.5f);
            float size = _dotSizePixels > 0 ? _dotSizePixels : Math.Max(10f, Math.Min(stepX, stepY) * 0.6f);

            using (Brush brush = new SolidBrush(Color.Black))
            {
                g.FillEllipse(brush, centerX - (size / 2f), centerY - (size / 2f), size, size);
            }
        }

        private static OmrRegion LoadOmrInfoRegion(string path)
        {
            XDocument document = XDocument.Load(path);
            XElement element = document.Descendants("omrInfo").FirstOrDefault(region => string.Equals(region.Attribute("memberName")?.Value, "omrNo", StringComparison.OrdinalIgnoreCase));
            return ParseRegion(element);
        }

        private static List<OmrRegion> LoadRegionsForPage(string path, string pageId)
        {
            XDocument document = XDocument.Load(path);
            XElement tag = document.Descendants("omr").FirstOrDefault(omr => string.Equals(omr.Attribute("omrNo")?.Value, pageId, StringComparison.OrdinalIgnoreCase));
            if (tag == null) return null;
            return tag.Descendants("region").Select(ParseRegion).Where(region => region != null).ToList();
        }

        private static OmrRegion ParseRegion(XElement element)
        {
            if (element == null) return null;

            return new OmrRegion
            {
                MemberName = element.Attribute("memberName")?.Value,
                Direction = element.Attribute("direction")?.Value ?? "y",
                CharSet = element.Attribute("charSet")?.Value,
                Lines = ParseInt(element.Attribute("lines")?.Value, 1),
                StartX = ParseInt(element.Element("startCircle")?.Attribute("x")?.Value, 1),
                StartY = ParseInt(element.Element("startCircle")?.Attribute("y")?.Value, 1),
                PaddingX = ParseFloat(element.Element("startPadding")?.Attribute("x")?.Value, 0f),
                PaddingY = ParseFloat(element.Element("startPadding")?.Attribute("y")?.Value, 0f),
                SpacingX = ParseFloat(element.Element("spacing")?.Attribute("x")?.Value, 0f),
                SpacingY = ParseFloat(element.Element("spacing")?.Attribute("y")?.Value, 0f),
                ResultCountCellX = ParseFloat(element.Attribute("resultCountCellX")?.Value, 0f),
                ResultCountCellY = ParseFloat(element.Attribute("resultCountCellY")?.Value, 0f),
                BlackPixelPercent = ParseDouble(element.Attribute("blackPixelPercent")?.Value, 20d),
                IsVirtual = string.Equals(element.Attribute("isVirtual")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                DefaultValue = element.Attribute("defaultValue")?.Value,
                LineStart = ParseInt(element.Attribute("lineStart")?.Value, 0)
            };
        }

        private static GridSystemData DetectAndCalculateGrid(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            Rectangle leftTop = FindMarkerRect(bmp, 0, 0, width / 5, height / 4, true, 0);
            Rectangle rightTop = FindMarkerRect(bmp, width - (width / 5), 0, width, height / 4, true, 0);
            if (leftTop.Width == 0 || rightTop.Width == 0)
            {
                return null;
            }

            // Search the full column below the top marker rather than only the
            // bottom quarter: on a half-page OMR (question paper underneath)
            // the bottom fiducial sits near the middle of the sheet, and a
            // fixed bottom-quarter window locks onto text instead.
            int expectedMarker = Math.Max(1, (leftTop.Width + leftTop.Height) / 2);
            Rectangle leftBottom = FindMarkerRect(bmp, Math.Max(0, leftTop.X - 20), leftTop.Bottom + 10,
                Math.Min(width, leftTop.Right + 20), height, false, expectedMarker);
            if (leftBottom.Width == 0)
            {
                return null;
            }

            int scanX = ((leftTop.X + leftBottom.X) / 2) + (leftTop.Width / 2);
            List<Rectangle> gridRows = FindTimingMarks(bmp, scanX, leftTop.Bottom + 5, leftBottom.Top - 5);
            if (gridRows.Count < 2)
            {
                return null;
            }

            GridSystemData data = new GridSystemData();
            data.YLines.Add(leftTop.Bottom);
            foreach (Rectangle row in gridRows) data.YLines.Add(row.Top);
            data.YLines.Add(leftBottom.Top);

            float totalDistanceY = gridRows.Last().Top - gridRows.First().Top;
            data.StepY = totalDistanceY / (gridRows.Count - 1);
            data.StepX = data.StepY;
            data.SheetBounds = Rectangle.FromLTRB(leftTop.X, leftTop.Y, rightTop.Right, leftBottom.Bottom);

            float currentX = leftTop.Right;
            while (currentX <= rightTop.Left + 2)
            {
                data.XLines.Add(currentX);
                currentX += data.StepX;
            }

            return data;
        }

        private static string DetectPageIdFromImage(Bitmap bmp, GridSystemData grid, OmrRegion region)
        {
            if (region == null) return "1";

            // Only an explicit defaultValue is a legitimate fallback. Guessing
            // "1" when the page-number bubble cannot be read is how a written
            // answer sheet ends up covered in MCQ dots, so an unreadable
            // bubbled design returns null and the page is skipped instead.
            string fallback = string.IsNullOrWhiteSpace(region.DefaultValue) ? null : region.DefaultValue.Trim();

            // <omrInfo isVirtual="true" defaultValue="2" /> carries no bubbles:
            // the page id is stated in the XML rather than read off the sheet.
            if (region.IsVirtual || string.IsNullOrWhiteSpace(region.CharSet)) return fallback ?? "1";
            if (region.StartX > grid.XLines.Count || region.StartY > grid.YLines.Count) return fallback;

            string[] pageIds = ParseCharSet(region.CharSet);
            if (pageIds.Length == 1) return pageIds[0];

            int bestMatchIndex = -1;
            double maxDarkness = -1d;

            // A page-id mark is not always a full grid cell: resultCountCellX/Y
            // give its real size, so the pitch between candidates is
            // (cell + spacing) and the sample point is the cell's own centre.
            // Assuming a 1-cell box made the Engineering sheet sample the gap
            // between the two marks and read neither.
            float cellX = region.ResultCountCellX > 0 ? region.ResultCountCellX : 1.0f;
            float cellY = region.ResultCountCellY > 0 ? region.ResultCountCellY : 1.0f;

            for (int i = 0; i < pageIds.Length; i++)
            {
                float originX = grid.XLines[region.StartX - 1];
                float originY = grid.YLines[region.StartY - 1];
                float checkX = originX + (region.PaddingX * grid.StepX);
                float checkY = originY + (region.PaddingY * grid.StepY);

                if (string.Equals(region.Direction, "x", StringComparison.OrdinalIgnoreCase))
                {
                    checkX += i * grid.StepX * (cellX + region.SpacingX);
                }
                else
                {
                    checkY += i * grid.StepY * (cellY + region.SpacingY);
                }

                double darkness = GetDarknessPercentage(bmp,
                    (int)(checkX + (cellX * grid.StepX) / 2f),
                    (int)(checkY + (cellY * grid.StepY) / 2f), 10);
                if (darkness > region.BlackPixelPercent && darkness > maxDarkness)
                {
                    maxDarkness = darkness;
                    bestMatchIndex = i;
                }
            }

            return bestMatchIndex >= 0 ? pageIds[bestMatchIndex] : fallback;
        }

        /// <summary>
        /// The page id that starts a new student, or null when every sheet does
        /// (a virtual or single-page design). Used instead of hard-coding "1",
        /// which silently broke designs numbered differently.
        /// </summary>
        private static string FirstPageId(OmrRegion omrInfo)
        {
            if (omrInfo == null) return null;
            if (omrInfo.IsVirtual || string.IsNullOrWhiteSpace(omrInfo.CharSet)) return null;

            string[] ids = ParseCharSet(omrInfo.CharSet);
            return ids.Length > 1 ? ids[0] : null;
        }

        /// <summary>
        /// The grid the XML was authored against (design/@gridX, @gridY).
        /// Zero when the attribute is absent, which disables the check.
        /// </summary>
        private static void LoadDesignGrid(string path, out int gridX, out int gridY)
        {
            gridX = 0;
            gridY = 0;

            XElement design = XDocument.Load(path).Descendants("design").FirstOrDefault();
            if (design == null) return;

            gridX = ParseInt(design.Attribute("gridX")?.Value, 0);
            gridY = ParseInt(design.Attribute("gridY")?.Value, 0);
        }

        /// <summary>The omrNo values the XML actually defines, for error messages.</summary>
        private static string[] AvailablePageIds(string path)
        {
            return XDocument.Load(path).Descendants("omr")
                .Select(omr => omr.Attribute("omrNo")?.Value)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToArray();
        }

        private static string[] ParseCharSet(string charSet)
        {
            if (string.IsNullOrWhiteSpace(charSet)) return new[] { "1" };
            if (charSet.Contains(","))
            {
                return charSet.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
            }

            return charSet.Select(ch => ch.ToString()).ToArray();
        }

        private static double GetDarknessPercentage(Bitmap bmp, int cx, int cy, int radius)
        {
            int black = 0;
            int total = 0;
            int startX = Math.Max(0, cx - radius);
            int startY = Math.Max(0, cy - radius);
            int endX = Math.Min(bmp.Width, cx + radius);
            int endY = Math.Min(bmp.Height, cy + radius);
            Rectangle rect = new Rectangle(startX, startY, endX - startX, endY - startY);
            BitmapData bitmapData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            unsafe
            {
                byte* ptr = (byte*)bitmapData.Scan0;
                for (int y = 0; y < rect.Height; y++)
                {
                    byte* row = ptr + (y * bitmapData.Stride);
                    for (int x = 0; x < rect.Width; x++)
                    {
                        if (row[x * 3] < 80) black++;
                        total++;
                    }
                }
            }

            bmp.UnlockBits(bitmapData);
            return total > 0 ? (double)black / total * 100d : 0d;
        }

        private static List<Rectangle> FindTimingMarks(Bitmap bmp, int scanX, int startY, int endY)
        {
            List<Rectangle> found = new List<Rectangle>();
            BitmapData bitmapData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            bool inBlock = false;
            int blockStart = -1;

            unsafe
            {
                byte* ptr = (byte*)bitmapData.Scan0;
                for (int y = startY; y < endY; y++)
                {
                    byte* pixel = ptr + (y * bitmapData.Stride) + (scanX * 3);
                    if (pixel[0] < 100)
                    {
                        if (!inBlock)
                        {
                            inBlock = true;
                            blockStart = y;
                        }
                    }
                    else if (inBlock)
                    {
                        if (y - blockStart > 3) found.Add(new Rectangle(scanX - 10, blockStart, 20, y - blockStart));
                        inBlock = false;
                    }
                }
            }

            bmp.UnlockBits(bitmapData);
            return found;
        }

        /// <summary>
        /// Finds the darkest solid square in a corner region.
        /// <paramref name="expectedSize"/> is the side length this marker
        /// should have (0 = unknown, the historic ~36px assumption). When it is
        /// known, components that are not within 30% of it, or that are not
        /// solid, are rejected outright - otherwise a stray letter from the
        /// question text can outscore the real fiducial simply by sitting
        /// closer to the edge of the search window.
        /// </summary>
        private static Rectangle FindMarkerRect(Bitmap bmp, int startX, int startY, int endX, int endY,
            bool isTopDown, int expectedSize)
        {
            int regionWidth = Math.Max(0, endX - startX);
            int regionHeight = Math.Max(0, endY - startY);
            if (regionWidth == 0 || regionHeight == 0)
            {
                return Rectangle.Empty;
            }

            BitmapData bitmapData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            bool[] visited = new bool[regionWidth * regionHeight];
            Rectangle bestRect = Rectangle.Empty;
            double bestScore = double.MaxValue;
            bool isRightSide = startX > (bmp.Width / 2);
            int targetX = isRightSide ? endX - 1 : startX;
            int targetY = isTopDown ? startY : endY - 1;

            unsafe
            {
                byte* ptr = (byte*)bitmapData.Scan0;
                int[] stack = new int[regionWidth * regionHeight];

                for (int ry = 0; ry < regionHeight; ry++)
                {
                    int y = startY + ry;
                    byte* row = ptr + (y * bitmapData.Stride);
                    for (int rx = 0; rx < regionWidth; rx++)
                    {
                        int visitIndex = (ry * regionWidth) + rx;
                        if (visited[visitIndex])
                        {
                            continue;
                        }

                        visited[visitIndex] = true;

                        int x = startX + rx;
                        if (row[x * 3] >= 80)
                        {
                            continue;
                        }

                        int stackCount = 0;
                        stack[stackCount++] = visitIndex;
                        int minRx = rx;
                        int maxRx = rx;
                        int minRy = ry;
                        int maxRy = ry;
                        int blackPixels = 0;

                        while (stackCount > 0)
                        {
                            int current = stack[--stackCount];
                            int currentRx = current % regionWidth;
                            int currentRy = current / regionWidth;
                            int currentX = startX + currentRx;
                            int currentY = startY + currentRy;
                            byte* currentRow = ptr + (currentY * bitmapData.Stride);
                            if (currentRow[currentX * 3] >= 80)
                            {
                                continue;
                            }

                            blackPixels++;
                            if (currentRx < minRx) minRx = currentRx;
                            if (currentRx > maxRx) maxRx = currentRx;
                            if (currentRy < minRy) minRy = currentRy;
                            if (currentRy > maxRy) maxRy = currentRy;

                            if (currentRx > 0)
                            {
                                int left = current - 1;
                                if (!visited[left])
                                {
                                    visited[left] = true;
                                    stack[stackCount++] = left;
                                }
                            }

                            if (currentRx + 1 < regionWidth)
                            {
                                int right = current + 1;
                                if (!visited[right])
                                {
                                    visited[right] = true;
                                    stack[stackCount++] = right;
                                }
                            }

                            if (currentRy > 0)
                            {
                                int up = current - regionWidth;
                                if (!visited[up])
                                {
                                    visited[up] = true;
                                    stack[stackCount++] = up;
                                }
                            }

                            if (currentRy + 1 < regionHeight)
                            {
                                int down = current + regionWidth;
                                if (!visited[down])
                                {
                                    visited[down] = true;
                                    stack[stackCount++] = down;
                                }
                            }
                        }

                        int componentWidth = (maxRx - minRx) + 1;
                        int componentHeight = (maxRy - minRy) + 1;
                        if (blackPixels < 80 || componentWidth < 10 || componentHeight < 10)
                        {
                            continue;
                        }

                        double aspectRatio = (double)Math.Max(componentWidth, componentHeight) / Math.Max(1, Math.Min(componentWidth, componentHeight));
                        double fillRatio = (double)blackPixels / (componentWidth * componentHeight);

                        if (expectedSize > 0)
                        {
                            double tolerance = expectedSize * 0.30;
                            if (Math.Abs(componentWidth - expectedSize) > tolerance) continue;
                            if (Math.Abs(componentHeight - expectedSize) > tolerance) continue;
                            if (fillRatio < 0.6) continue;
                        }

                        int nominalSize = expectedSize > 0 ? expectedSize : 36;
                        double sizePenalty = Math.Abs(componentWidth - nominalSize) + Math.Abs(componentHeight - nominalSize);
                        int markerCenterX = startX + minRx + (componentWidth / 2);
                        int markerCenterY = startY + minRy + (componentHeight / 2);
                        double distancePenalty = Math.Abs(markerCenterX - targetX) + Math.Abs(markerCenterY - targetY);
                        double score = distancePenalty + (Math.Abs(aspectRatio - 1.0) * 200d) + (Math.Abs(fillRatio - 1.0) * 150d) + (sizePenalty * 2d);

                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestRect = new Rectangle(startX + minRx, startY + minRy, componentWidth, componentHeight);
                        }
                    }
                }
            }

            bmp.UnlockBits(bitmapData);
            return bestRect;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static float ParseFloat(string value, float fallback)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static double ParseDouble(string value, double fallback)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, new OmrLogEventArgs(message));
        }
    }
}
