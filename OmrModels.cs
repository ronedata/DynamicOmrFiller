using System;
using System.Collections.Generic;
using System.Drawing;

namespace DynamicOmrFiller
{
    public class GridSystemData
    {
        public List<float> XLines { get; } = new List<float>();
        public List<float> YLines { get; } = new List<float>();
        public float StepX { get; set; }
        public float StepY { get; set; }

        /// <summary>
        /// Bounding box of the three fiducial markers: the OMR sheet's area on
        /// the page. On a half-page design the rest of the page is the question
        /// paper, which PDF output trims away.
        /// </summary>
        public Rectangle SheetBounds { get; set; }
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

        /// <summary>omrInfo isVirtual="true": the page id is fixed in the XML, not read off the sheet.</summary>
        public bool IsVirtual { get; set; }

        /// <summary>omrInfo defaultValue: the page id to use when nothing is detected.</summary>
        public string DefaultValue { get; set; }

        /// <summary>region lineStart: the real question number of this region's first row (0 = unset).</summary>
        public int LineStart { get; set; }
    }

    /// <summary>Where the sheets come from.</summary>
    public enum OmrInputMode
    {
        SingleImage = 0,
        Folder = 1,
        Pdf = 2
    }

    public class OmrBatchOptions
    {
        public string InputPath { get; set; }
        public string OutputFolder { get; set; }
        public string XmlPath { get; set; }
        public string RollNumbersInput { get; set; }
        public string RegistrationNumbersInput { get; set; }
        public string SaqExtraText { get; set; }
        public OmrInputMode InputMode { get; set; }
        public int DotSizePixels { get; set; }
        public int SaqFontSizePixels { get; set; }

        /// <summary>
        /// Write the auto-generated cell label ("SaqAnswer1") under the SAQ
        /// text. Off leaves the whole cell to the text.
        /// </summary>
        public bool WriteSaqLabel { get; set; } = true;

        /// <summary>Font size for the auto-generated cell label ("SaqAnswer1"). 0 = auto from the grid.</summary>
        public int SaqLabelFontSizePixels { get; set; }

        /// <summary>Where the SAQ text sits inside its cell.</summary>
        public StringAlignment SaqAlignHorizontal { get; set; } = StringAlignment.Center;
        public StringAlignment SaqAlignVertical { get; set; } = StringAlignment.Center;
        public bool FillQuestionQuality { get; set; }

        /// <summary>
        /// Also print the roll / registration digits in the write-in boxes
        /// above the bubble grid, the way a candidate fills them by hand.
        /// </summary>
        public bool WriteStudentNumbers { get; set; } = true;

        /// <summary>
        /// Trim PDF output to the OMR sheet area. A PDF page is the whole A4
        /// sheet, so without this the question half is kept as well.
        /// </summary>
        public bool CropPdfToSheet { get; set; } = true;

        /// <summary>
        /// How far above the first bubble row the written digits sit, in grid
        /// rows. 1.0 centres them in the printed box row of a standard sheet;
        /// larger moves them further up, smaller moves them down.
        /// </summary>
        public float WriteOffsetRows { get; set; } = 1.0f;
        public string AnswerKeyInput { get; set; }
        public bool ExportAnswerKey { get; set; }

        /// <summary>
        /// What to do with MCQ questions the answer key does not list:
        /// true fills them randomly (default), false leaves them blank.
        /// </summary>
        public bool UnlistedAnswersRandom { get; set; } = true;
    }

    public class OmrBatchResult
    {
        public int TotalImages { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }

        /// <summary>PDF pages that carry no matching OMR sheet - question pages, typically.</summary>
        public int SkippedCount { get; set; }
        public List<string> OutputFiles { get; } = new List<string>();
        public string AnswerKeyFile { get; set; }
    }

    /// <summary>One filled answer, recorded for the exported answer key CSV.</summary>
    public class AnswerKeyEntry
    {
        public string OutputFile { get; set; }
        public string Roll { get; set; }
        public string Registration { get; set; }
        public string PageId { get; set; }
        public string Region { get; set; }
        public int Question { get; set; }
        public string Answer { get; set; }
    }

    public sealed class OmrLogEventArgs : EventArgs
    {
        public OmrLogEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }
}
