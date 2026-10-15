using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OmrSmartBatchSystem
{
    // --- DATA CLASSES ---
    public class GridSystemData
    {
        public List<float> XLines { get; set; } = new List<float>();
        public List<float> YLines { get; set; } = new List<float>();
        public float StepX { get; set; }
        public float StepY { get; set; }
    }

    public class OmrRegion
    {
        public string MemberName { get; set; }
        public string Direction { get; set; }
        public string CharSet { get; set; }
        public int StartX { get; set; }
        public int StartY { get; set; }
        public float PaddingX { get; set; }
        public float PaddingY { get; set; }
        public float SpacingX { get; set; }
        public float SpacingY { get; set; }
        public int Lines { get; set; }
        public float ResultCountCellX { get; set; }
        public float ResultCountCellY { get; set; }
        public double BlackPixelPercent { get; set; } = 20.0;
    }

    class Program
    {
        // গ্লোবাল ভেরিয়েবল বা মেইনের স্কোপে ডাটা ট্র্যাকিং এর জন্য
        static void Main(string[] args)
        {
            // ইনপুট (ফাইল পাথ বা ফোল্ডার পাথ)
            string inputPath = @"C:\Users\rone\Downloads\mcq\New folder";
            string outputFolder = @"C:\Users\rone\Downloads\mcq\output";
            string xmlPath = @"C:\Users\rone\Downloads\BUET_MCQ_50_SAQ_18_A4_A3_omr_config.xml";

            // একাধিক রোল ও রেজিস্ট্রেশন কমা দিয়ে আলাদা করা
            string rollNumbersInput = "10205026680,10205026681";
            string registrationNumbersInput = "1607495,1607496";
            string saqExtraText = "ইরানের নতুন সর্বোচ্চ নেতা মোজতবা খামেনি যুদ্ধকালীন চোটে আহত হলেও বর্তমানে ‘নিরাপদ আছেন।\r\n\r\nআজ বুধবার ইরানের প্রেসিডেন্ট মাসুদ পেজেশকিয়ানের ছেলে ও সরকারের উপদেষ্টা ইউসেফ পেজেশকিয়ান এ তথ্য নিশ্চিত করেছেন।\r\n\r\nনিজের টেলিগ্রাম চ্যানেলে দেওয়া এক পোস্টে ইউসেফ পেজেশকিয়ান বলেন, ‘আমি খবর পেয়েছিলাম যে মোজতবা খামেনি আহত হয়েছেন। বিষয়টি নিয়ে সংশ্লিষ্টদের সঙ্গে যোগাযোগ করলে তাঁরা আমাকে জানিয়েছেন, আল্লাহর রহমতে তিনি নিরাপদ ও সুস্থ আছেন।’";

            try
            {
                if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
                if (!File.Exists(xmlPath)) { Console.WriteLine("XML not found."); return; }

                // ১. রোল এবং রেজিস্ট্রেশন পার্স করে কিউ (Queue) বা অ্যারেতে নেওয়া
                string[] rollList = rollNumbersInput.Split(',');
                string[] regList = registrationNumbersInput.Split(',');

                // ট্র্যাকার ভেরিয়েবল
                int studentIndex = 0;
                string currentRoll = rollList.Length > 0 ? rollList[0] : "";
                string currentReg = regList.Length > 0 ? regList[0] : "";

                // ২. ফাইল নাকি ফোল্ডার চেক করা
                string[] imageFiles;
                if (Directory.Exists(inputPath))
                {
                    // ফোল্ডার হলে সব ইমেজ ফাইল নেওয়া (jpg, png, jpeg)
                    imageFiles = Directory.GetFiles(inputPath, "*.*")
                                          .Where(s => s.EndsWith(".jpg") || s.EndsWith(".jpeg") || s.EndsWith(".png") || s.EndsWith(".bmp"))
                                          .ToArray();
                    Console.WriteLine($"Processing Folder: Found {imageFiles.Length} images.");
                }
                else if (File.Exists(inputPath))
                {
                    // সিঙ্গেল ফাইল
                    imageFiles = new string[] { inputPath };
                }
                else
                {
                    Console.WriteLine("Input path not found.");
                    return;
                }

                // ৩. প্রতিটি ইমেজের জন্য লুপ চালানো
                foreach (string imgFile in imageFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(imgFile);
                    string ext = Path.GetExtension(imgFile);
                    string uniqueTimestamp = DateTime.Now.ToString("HHmmss_fff");
                    string outputFilePath = Path.Combine(outputFolder, $"{fileName}_edit_{uniqueTimestamp}{ext}");

                    Console.WriteLine($"\nProcessing: {Path.GetFileName(imgFile)}");

                    // Process Method কল করা (রেফারেন্স সহ)
                    ProcessOmrImage(
                        imgFile,
                        outputFilePath,
                        xmlPath,
                        rollList,
                        regList,
                        ref studentIndex,
                        ref currentRoll,
                        ref currentReg,
                        saqExtraText
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Critical Error: " + ex.Message);
            }
        }

        static void ProcessOmrImage(
            string inputPath,
            string outputPath,
            string xmlPath,
            string[] rollList,
            string[] regList,
            ref int studentIndex,
            ref string currentRoll,
            ref string currentReg,
            string saqExtra)
        {
            using (Bitmap originalBmp = new Bitmap(inputPath))
            using (Bitmap workBmp = new Bitmap(originalBmp))
            {
                workBmp.SetResolution(originalBmp.HorizontalResolution, originalBmp.VerticalResolution);

                using (Graphics g = Graphics.FromImage(workBmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    // Grid Detection
                    GridSystemData gridData = DetectAndCalculateGrid(workBmp);

                    if (gridData != null)
                    {
                        //DrawGridMesh(g, workBmp.Width, gridData);

                        // Detect Page ID
                        OmrRegion omrNoRegion = LoadOmrInfoRegion(xmlPath);
                        string detectedPageId = DetectPageIdFromImage(workBmp, gridData, omrNoRegion);
                        Console.WriteLine($"  -> Detected Page ID: {detectedPageId}");

                        // =========================================================
                        // CORE LOGIC: Handle Multiple Students based on Page ID
                        // =========================================================
                        if (detectedPageId == "1")
                        {
                            // যদি Page 1 হয়, তাহলে নতুন রোল/রেজি অ্যাসাইন হবে (যদি লিস্টে বাকি থাকে)
                            // প্রথমবার (studentIndex=0) আমরা অলরেডি ইনিশিলাইজ করেছি, তাই কন্ডিশন চেক করব
                            // এখানে লজিক: আমরা কি এই পেজের জন্য বর্তমান ইনডেক্স ব্যবহার করব নাকি বাড়াবো?
                            // সহজ লজিক: প্রতিবার Page 1 পেলে আমরা লিস্ট থেকে ডাটা নিব।

                            if (studentIndex < rollList.Length)
                            {
                                currentRoll = rollList[studentIndex];
                                currentReg = (studentIndex < regList.Length) ? regList[studentIndex] : "";

                                Console.WriteLine($"  -> New Student Detected. Assigned Roll: {currentRoll}");

                                // পরের পেজ ১ এর জন্য ইনডেক্স বাড়িয়ে রাখা হলো
                                studentIndex++;
                            }
                            else
                            {
                                Console.WriteLine("  -> Warning: No more roll numbers in list. Using last one.");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"  -> Continuing with Roll: {currentRoll} (Page {detectedPageId})");
                        }

                        // Load Regions
                        List<OmrRegion> activeRegions = LoadRegionsForPage(xmlPath, detectedPageId);

                        foreach (var region in activeRegions)
                        {
                            if (region.MemberName == "rollNo")
                            {
                                DrawMappedField(g, gridData, region, currentRoll);
                            }
                            else if (region.MemberName == "registrationNo")
                            {
                                DrawMappedField(g, gridData, region, currentReg);
                            }
                            else if (region.MemberName.StartsWith("answer"))
                            {
                                DrawRandomAnswers(g, gridData, region);
                            }
                            else if (region.MemberName.StartsWith("SaqAnswer"))
                            {
                                DrawSaqMappingLabel(g, gridData, region, saqExtra);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("  -> FAILED: Grid detection failed.");
                    }
                }
                workBmp.Save(outputPath, ImageFormat.Jpeg);
            }
        }

        // =========================================================
        // DRAWING METHODS
        // =========================================================
        // =========================================================
        // UPDATED: DRAW SAQ LABEL WITH BOUNDED TEXT WRAPPING
        // =========================================================
        static void DrawSaqMappingLabel(Graphics g, GridSystemData grid, OmrRegion region, string extraText = null)
        {
            if (region.StartX > grid.XLines.Count || region.StartY > grid.YLines.Count) return;

            float originX = grid.XLines[region.StartX - 1];
            float originY = grid.YLines[region.StartY - 1];
            float startPixelX = originX + (region.PaddingX * grid.StepX);
            float startPixelY = originY + (region.PaddingY * grid.StepY);

            float extraFontSize = Math.Max(14f, Math.Min(26f, grid.StepY * 0.6f));
            float labelFontSize = Math.Max(10f, Math.Min(18f, grid.StepY * 0.4f));

            using (Font extraFont = new Font("Arial", extraFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font labelFont = new Font("Arial", labelFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(Color.Black))
            using (StringFormat extraFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.Word })
            using (StringFormat labelFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                // লজিক চেক: resultCountCell ডাটা আছে কিনা (বড় এরিয়া)
                if (region.ResultCountCellX > 0 && region.ResultCountCellY > 0)
                {
                    float totalWidth = region.ResultCountCellX * grid.StepX;
                    float totalHeight = region.ResultCountCellY * grid.StepY;

                    // --- EXTRA TEXT LOGIC (BOUNDED RECT — text wrap হবে) ---
                    if (!string.IsNullOrEmpty(extraText))
                    {
                        // Extra Text উপরের অর্ধেকে (RectangleF দিয়ে bound করা, লম্বা text wrap হবে)
                        float halfHeight = totalHeight / 2.0f;
                        RectangleF extraRect = new RectangleF(startPixelX, startPixelY, totalWidth, halfHeight);
                        g.DrawString(extraText, extraFont, brush, extraRect, extraFormat);

                        // Main Label নিচের অর্ধেকে
                        RectangleF labelRect = new RectangleF(startPixelX, startPixelY + halfHeight, totalWidth, halfHeight);
                        g.DrawString(region.MemberName, labelFont, brush, labelRect, labelFormat);
                    }
                    else
                    {
                        RectangleF fullRect = new RectangleF(startPixelX, startPixelY, totalWidth, totalHeight);
                        g.DrawString(region.MemberName, labelFont, brush, fullRect, labelFormat);
                    }
                }
                else
                {
                    // Fallback Logic (Line by Line) — RectangleF bound সহ
                    int count = region.Lines > 0 ? region.Lines : 1;
                    // Available width: left marker থেকে right marker পর্যন্ত
                    float availableWidth = grid.XLines.Last() - grid.XLines[region.StartX - 1];

                    for (int i = 0; i < count; i++)
                    {
                        float spacingFactorY = 1.0f + region.SpacingY;
                        float cellTopY = startPixelY + (i * grid.StepY * spacingFactorY);
                        float cellHeight = grid.StepY;

                        string label = count > 1 ? $"{region.MemberName} {i + 1}" : region.MemberName;

                        // --- EXTRA TEXT LOGIC (BOUNDED RECT) ---
                        if (!string.IsNullOrEmpty(extraText))
                        {
                            float halfH = cellHeight / 2.0f;
                            RectangleF extraRect = new RectangleF(startPixelX, cellTopY, availableWidth, halfH);
                            g.DrawString(extraText, extraFont, brush, extraRect, extraFormat);

                            RectangleF labelRect = new RectangleF(startPixelX, cellTopY + halfH, availableWidth, halfH);
                            g.DrawString(label, labelFont, brush, labelRect, labelFormat);
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

        static void DrawMappedField(Graphics g, GridSystemData grid, OmrRegion region, string value)
        {
            if (region.StartX > grid.XLines.Count || region.StartY > grid.YLines.Count) return;
            float originX = grid.XLines[region.StartX - 1];
            float originY = grid.YLines[region.StartY - 1];

            for (int i = 0; i < value.Length; i++)
            {
                char digit = value[i];
                int charIndex = region.CharSet.IndexOf(digit);
                if (charIndex == -1) continue;
                float lineX = originX + (region.PaddingX * grid.StepX) + (i * grid.StepX * (1.0f + region.SpacingX));
                float lineY = originY + (region.PaddingY * grid.StepY) + (charIndex * grid.StepY * (1.0f + region.SpacingY));
                DrawDot(g, lineX, lineY, grid.StepX, grid.StepY);
            }
        }

        static void DrawRandomAnswers(Graphics g, GridSystemData grid, OmrRegion region)
        {
            Random rnd = new Random();
            float originX = grid.XLines[region.StartX - 1];
            float originY = grid.YLines[region.StartY - 1];
            List<string> resultLog = new List<string>();

            for (int q = 0; q < region.Lines; q++)
            {
                int ansIndex = rnd.Next(0, region.CharSet.Length);
                float lineX = originX + (region.PaddingX * grid.StepX) + (ansIndex * grid.StepX * (1.0f + region.SpacingX));
                float lineY = originY + (region.PaddingY * grid.StepY) + (q * grid.StepY * (1.0f + region.SpacingY));
                DrawDot(g, lineX, lineY, grid.StepX, grid.StepY);

                char answerChar = region.CharSet.Length > ansIndex ? region.CharSet[ansIndex] : '?';
                resultLog.Add($"{q + 1}-{answerChar}");
            }
            Console.WriteLine($"  -> Answers ({region.MemberName}): {string.Join(", ", resultLog)}");
        }

        static void DrawDot(Graphics g, float x, float y, float stepX, float stepY)
        {
            float centerX = x + (stepX * 0.5f);
            float centerY = y + (stepY * 0.5f);
            int size = 30;
            using (Brush b = new SolidBrush(Color.Black))
                g.FillEllipse(b, centerX - (size / 2), centerY - (size / 2), size, size);
        }

        static void DrawGridMesh(Graphics g, int imgWidth, GridSystemData data)
        {
            using (Pen redPen = new Pen(Color.FromArgb(80, Color.Red), 1))
            using (Font font = new Font("Arial", 7))
            using (Brush textBrush = new SolidBrush(Color.Blue))
            using (StringFormat centerAlign = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
            using (StringFormat leftAlign = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < data.YLines.Count; i++)
                {
                    float y = data.YLines[i];
                    g.DrawLine(redPen, 0, y, imgWidth, y);
                    if (i < data.YLines.Count - 1)
                    {
                        float midY = (y + data.YLines[i + 1]) / 2.0f;
                        g.DrawString((i + 1).ToString("D2"), font, textBrush, 2, midY, leftAlign);
                    }
                }
                float topY = data.YLines.First();
                float btmY = data.YLines.Last();
                for (int i = 0; i < data.XLines.Count; i++)
                {
                    float x = data.XLines[i];
                    g.DrawLine(redPen, x, topY, x, btmY);
                    if (i < data.XLines.Count - 1)
                    {
                        float midX = (x + data.XLines[i + 1]) / 2.0f;
                        g.DrawString((i + 1).ToString("D2"), font, textBrush, midX, topY + 2, centerAlign);
                    }
                }
            }
        }

        // =========================================================
        // XML & DETECTION
        // =========================================================
        static OmrRegion LoadOmrInfoRegion(string path)
        {
            try { return ParseRegion(XDocument.Load(path).Descendants("omrInfo").FirstOrDefault(r => r.Attribute("memberName")?.Value == "omrNo")); }
            catch { return null; }
        }

        static List<OmrRegion> LoadRegionsForPage(string path, string pageId)
        {
            List<OmrRegion> regions = new List<OmrRegion>();
            try
            {
                var tag = XDocument.Load(path).Descendants("omr").FirstOrDefault(o => o.Attribute("omrNo")?.Value == pageId);
                if (tag != null) foreach (var elem in tag.Descendants("region")) regions.Add(ParseRegion(elem));
            }
            catch { }
            return regions;
        }

        static OmrRegion ParseRegion(XElement e)
        {
            if (e == null) return null;
            OmrRegion r = new OmrRegion();
            r.MemberName = e.Attribute("memberName")?.Value;
            r.Direction = e.Attribute("direction")?.Value ?? "y";
            r.CharSet = e.Attribute("charSet")?.Value;
            r.Lines = int.Parse(e.Attribute("lines")?.Value ?? "1");
            r.StartX = int.Parse(e.Element("startCircle")?.Attribute("x")?.Value ?? "1");
            r.StartY = int.Parse(e.Element("startCircle")?.Attribute("y")?.Value ?? "1");
            r.PaddingX = float.Parse(e.Element("startPadding")?.Attribute("x")?.Value ?? "0");
            r.PaddingY = float.Parse(e.Element("startPadding")?.Attribute("y")?.Value ?? "0");
            r.SpacingX = float.Parse(e.Element("spacing")?.Attribute("x")?.Value ?? "0");
            r.SpacingY = float.Parse(e.Element("spacing")?.Attribute("y")?.Value ?? "0");
            r.ResultCountCellX = float.Parse(e.Attribute("resultCountCellX")?.Value ?? "0");
            r.ResultCountCellY = float.Parse(e.Attribute("resultCountCellY")?.Value ?? "0");
            r.BlackPixelPercent = double.Parse(e.Attribute("blackPixelPercent")?.Value ?? "20");
            return r;
        }

        static GridSystemData DetectAndCalculateGrid(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            Rectangle lt = FindMarkerRect(bmp, 0, 0, w / 5, h / 4, true);
            Rectangle rt = FindMarkerRect(bmp, w - (w / 5), 0, w, h / 4, true);
            Console.WriteLine($"  [Grid] LT marker: {lt}, RT marker: {rt}");
            if (lt.Width == 0 || rt.Width == 0)
            {
                Console.WriteLine("  [Grid] Top markers not found.");
                return null;
            }

            Rectangle lb = FindMarkerRect(bmp, Math.Max(0, lt.X - 20), h * 3 / 4, Math.Min(w, lt.Right + 20), h, false);
            Console.WriteLine($"  [Grid] LB marker: {lb}");
            if (lb.Width == 0)
            {
                Console.WriteLine("  [Grid] Left-bottom marker not found.");
                return null;
            }

            int scanX = (lt.X + lb.X) / 2 + (lt.Width / 2);
            List<Rectangle> gridRows = FindTimingMarks(bmp, scanX, lt.Bottom + 5, lb.Top - 5);
            Console.WriteLine($"  [Grid] scanX={scanX}, scanY={lt.Bottom + 5}..{lb.Top - 5}, timing marks found={gridRows.Count}");
            if (gridRows.Count < 2)
            {
                Console.WriteLine("  [Grid] Not enough timing marks to calculate step size.");
                return null;
            }

            GridSystemData data = new GridSystemData();
            data.YLines.Add(lt.Bottom);
            foreach (var row in gridRows) data.YLines.Add(row.Top);
            data.YLines.Add(lb.Top);

            float totalDistY = gridRows.Last().Top - gridRows.First().Top;
            data.StepY = totalDistY / (gridRows.Count - 1);
            data.StepX = data.StepY;
            Console.WriteLine($"  [Grid] totalDistY={totalDistY}, StepY={data.StepY}, StepX={data.StepX}");

            float startX = lt.Right;
            float endLimitX = rt.Left;
            float currentX = startX;
            while (currentX <= endLimitX + 2) { data.XLines.Add(currentX); currentX += data.StepX; }
            return data;
        }

        static string DetectPageIdFromImage(Bitmap bmp, GridSystemData grid, OmrRegion region)
        {
            if (region == null) return "1";
            //string[] pageIds = string.IsNullOrEmpty(region.CharSet) ? new string[] { "1" } : region.CharSet.Select(c => c.ToString()).ToArray();
            string[] pageIds = ParseCharSet(region.CharSet);
            if (pageIds.Length == 1) return pageIds[0];
            int bestMatchIndex = -1;
            double maxDarkness = -1;
            for (int i = 0; i < pageIds.Length; i++)
            {
                float originX = grid.XLines[region.StartX - 1];
                float originY = grid.YLines[region.StartY - 1];
                float checkX = originX + (region.PaddingX * grid.StepX);
                float checkY = originY + (region.PaddingY * grid.StepY);
                if (region.Direction?.ToLower() == "x") checkX += (i * grid.StepX * (1.0f + region.SpacingX));
                else checkY += (i * grid.StepY * (1.0f + region.SpacingY));

                double darkness = GetDarknessPercentage(bmp, (int)(checkX + grid.StepX / 2), (int)(checkY + grid.StepY / 2), 10);
                if (darkness > region.BlackPixelPercent && darkness > maxDarkness) { maxDarkness = darkness; bestMatchIndex = i; }
            }
            return bestMatchIndex != -1 ? pageIds[bestMatchIndex] : "1";
        }

        static string[] ParseCharSet(string charSet)
        {
            if (string.IsNullOrWhiteSpace(charSet))
                return new[] { "1" };

            if (charSet.Contains(","))
                return charSet.Split(',', (char)StringSplitOptions.RemoveEmptyEntries)
                              .Select(x => x.Trim())
                              .ToArray();

            return charSet.Select(c => c.ToString()).ToArray();
        }

        static double GetDarknessPercentage(Bitmap bmp, int cx, int cy, int r)
        {
            int black = 0, total = 0;
            int sx = Math.Max(0, cx - r), sy = Math.Max(0, cy - r), ex = Math.Min(bmp.Width, cx + r), ey = Math.Min(bmp.Height, cy + r);
            BitmapData bData = bmp.LockBits(new Rectangle(sx, sy, ex - sx, ey - sy), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            unsafe
            {
                byte* ptr = (byte*)bData.Scan0;
                for (int y = 0; y < (ey - sy); y++)
                {
                    byte* row = ptr + (y * bData.Stride);
                    for (int x = 0; x < (ex - sx); x++)
                    {
                        if (row[x * 3] < 80) black++;
                        total++;
                    }
                }
            }
            bmp.UnlockBits(bData);
            return total > 0 ? (double)black / total * 100 : 0;
        }

        static List<Rectangle> FindTimingMarks(Bitmap bmp, int scanX, int startY, int endY)
        {
            List<Rectangle> found = new List<Rectangle>();
            BitmapData bData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            bool inBlock = false; int blockStart = -1;
            unsafe
            {
                byte* ptr = (byte*)bData.Scan0;
                for (int y = startY; y < endY; y++)
                {
                    byte* pixel = ptr + (y * bData.Stride) + (scanX * 3);
                    if (pixel[0] < 100) { if (!inBlock) { inBlock = true; blockStart = y; } }
                    else if (inBlock) { if (y - blockStart > 3) found.Add(new Rectangle(scanX - 10, blockStart, 20, y - blockStart)); inBlock = false; }
                }
            }
            bmp.UnlockBits(bData);
            return found;
        }

        static Rectangle FindMarkerRect(Bitmap bmp, int startX, int startY, int endX, int endY, bool isTopDown)
        {
            int regionWidth = Math.Max(0, endX - startX);
            int regionHeight = Math.Max(0, endY - startY);
            if (regionWidth == 0 || regionHeight == 0) return Rectangle.Empty;

            BitmapData bData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            bool[] visited = new bool[regionWidth * regionHeight];
            Rectangle bestRect = Rectangle.Empty;
            double bestScore = double.MaxValue;
            bool isRightSide = startX > (bmp.Width / 2);
            int targetX = isRightSide ? endX - 1 : startX;
            int targetY = isTopDown ? startY : endY - 1;

            unsafe
            {
                byte* ptr = (byte*)bData.Scan0;
                int[] stack = new int[regionWidth * regionHeight];

                for (int ry = 0; ry < regionHeight; ry++)
                {
                    int y = startY + ry;
                    byte* row = ptr + (y * bData.Stride);
                    for (int rx = 0; rx < regionWidth; rx++)
                    {
                        int visitIndex = (ry * regionWidth) + rx;
                        if (visited[visitIndex]) continue;
                        visited[visitIndex] = true;

                        int x = startX + rx;
                        if (row[x * 3] >= 80) continue;

                        int stackCount = 0;
                        stack[stackCount++] = visitIndex;
                        int minRx = rx, maxRx = rx, minRy = ry, maxRy = ry;
                        int blackPixels = 0;

                        while (stackCount > 0)
                        {
                            int current = stack[--stackCount];
                            int currentRx = current % regionWidth;
                            int currentRy = current / regionWidth;
                            int currentX = startX + currentRx;
                            int currentY = startY + currentRy;
                            byte* currentRow = ptr + (currentY * bData.Stride);
                            if (currentRow[currentX * 3] >= 80) continue;

                            blackPixels++;
                            if (currentRx < minRx) minRx = currentRx;
                            if (currentRx > maxRx) maxRx = currentRx;
                            if (currentRy < minRy) minRy = currentRy;
                            if (currentRy > maxRy) maxRy = currentRy;

                            if (currentRx > 0)
                            {
                                int left = current - 1;
                                if (!visited[left]) { visited[left] = true; stack[stackCount++] = left; }
                            }
                            if (currentRx + 1 < regionWidth)
                            {
                                int right = current + 1;
                                if (!visited[right]) { visited[right] = true; stack[stackCount++] = right; }
                            }
                            if (currentRy > 0)
                            {
                                int up = current - regionWidth;
                                if (!visited[up]) { visited[up] = true; stack[stackCount++] = up; }
                            }
                            if (currentRy + 1 < regionHeight)
                            {
                                int down = current + regionWidth;
                                if (!visited[down]) { visited[down] = true; stack[stackCount++] = down; }
                            }
                        }

                        int width = (maxRx - minRx) + 1;
                        int height = (maxRy - minRy) + 1;
                        if (blackPixels < 80 || width < 10 || height < 10) continue;

                        double aspectRatio = (double)Math.Max(width, height) / Math.Max(1, Math.Min(width, height));
                        double fillRatio = (double)blackPixels / (width * height);
                        double sizePenalty = Math.Abs(width - 36) + Math.Abs(height - 36);
                        int markerCenterX = startX + minRx + (width / 2);
                        int markerCenterY = startY + minRy + (height / 2);
                        double distancePenalty = Math.Abs(markerCenterX - targetX) + Math.Abs(markerCenterY - targetY);
                        double score = distancePenalty + (Math.Abs(aspectRatio - 1.0) * 200) + (Math.Abs(fillRatio - 1.0) * 150) + (sizePenalty * 2);

                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestRect = new Rectangle(startX + minRx, startY + minRy, width, height);
                        }
                    }
                }
            }
            bmp.UnlockBits(bData);
            return bestRect;
        }
    }
}
