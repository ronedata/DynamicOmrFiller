using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading;

namespace DynamicOmrFiller
{
    /// <summary>
    /// Renders PDF pages to bitmaps using the PDF engine built into Windows
    /// (Windows.Data.Pdf), so the project keeps its "no NuGet, no native DLL"
    /// rule.
    ///
    /// It is all late-bound on purpose. The WinRT types resolve from the OS
    /// metadata in System32\WinMetadata without the Windows SDK installed, but
    /// the objects that come back are bare __ComObject with no projected
    /// interfaces - so every call has to go through the *declared* interface
    /// type, and an async operation's Status has to be read off IAsyncInfo
    /// rather than off the returned object.
    /// </summary>
    internal sealed class PdfPageSource : IDisposable
    {
        private static readonly Type StorageFileType = WinRt("Windows.Storage.StorageFile");
        private static readonly Type StorageFileInterface = WinRt("Windows.Storage.IStorageFile");
        private static readonly Type PdfDocumentType = WinRt("Windows.Data.Pdf.PdfDocument");
        private static readonly Type PdfPageType = WinRt("Windows.Data.Pdf.PdfPage");
        private static readonly Type RenderOptionsType = WinRt("Windows.Data.Pdf.PdfPageRenderOptions");
        private static readonly Type MemoryStreamType = WinRt("Windows.Storage.Streams.InMemoryRandomAccessStream");
        private static readonly Type RandomAccessStreamInterface = WinRt("Windows.Storage.Streams.IRandomAccessStream");
        private static readonly Type DataReaderType = WinRt("Windows.Storage.Streams.DataReader");
        private static readonly Type AsyncInfoType = WinRt("Windows.Foundation.IAsyncInfo");

        private readonly object _document;

        /// <summary>False when this Windows build cannot supply the PDF engine.</summary>
        public static bool IsAvailable
        {
            get
            {
                return StorageFileType != null && PdfDocumentType != null && PdfPageType != null
                    && RenderOptionsType != null && MemoryStreamType != null
                    && DataReaderType != null && AsyncInfoType != null;
            }
        }

        public static bool IsPdf(string path)
        {
            return !string.IsNullOrEmpty(path)
                && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        public PdfPageSource(string pdfPath)
        {
            if (!IsAvailable)
            {
                throw new InvalidOperationException(
                    "This copy of Windows does not provide the built-in PDF engine, so PDF input is unavailable.");
            }
            if (!File.Exists(pdfPath)) throw new FileNotFoundException("PDF not found.", pdfPath);

            object file = Invoke(StorageFileType, "GetFileFromPathAsync", null, null,
                new object[] { Path.GetFullPath(pdfPath) });
            _document = Invoke(PdfDocumentType, "LoadFromFileAsync", null,
                new[] { StorageFileInterface }, new[] { file });
        }

        public int PageCount
        {
            get { return (int)(uint)PdfDocumentType.GetProperty("PageCount").GetValue(_document, null); }
        }

        /// <summary>
        /// Renders one page at the given pixel width. 2480 px across an A4 page
        /// is 300 dpi, which is what the marker detection is tuned for.
        /// </summary>
        public Bitmap RenderPage(int pageIndex, int widthPixels)
        {
            object page = PdfDocumentType.GetMethod("GetPage").Invoke(_document, new object[] { (uint)pageIndex });

            object options = Activator.CreateInstance(RenderOptionsType);
            RenderOptionsType.GetProperty("DestinationWidth").SetValue(options, (uint)widthPixels, null);

            object stream = Activator.CreateInstance(MemoryStreamType);
            Invoke(PdfPageType, "RenderToStreamAsync", page,
                new[] { RandomAccessStreamInterface, RenderOptionsType }, new[] { stream, options });

            ulong size = (ulong)MemoryStreamType.GetProperty("Size").GetValue(stream, null);
            object input = MemoryStreamType.GetMethod("GetInputStreamAt").Invoke(stream, new object[] { 0UL });
            object reader = Activator.CreateInstance(DataReaderType, new[] { input });
            Invoke(DataReaderType, "LoadAsync", reader, null, new object[] { (uint)size });

            byte[] bytes = new byte[size];
            DataReaderType.GetMethod("ReadBytes").Invoke(reader, new object[] { bytes });

            // Copy off the stream so the bitmap does not hold it open.
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Image rendered = Image.FromStream(ms))
            {
                if (rendered.Width == widthPixels)
                {
                    Bitmap copy = new Bitmap(rendered);
                    copy.SetResolution(300f, 300f);
                    return copy;
                }

                // Windows multiplies DestinationWidth by the display's scale
                // factor, so a 125% monitor hands back a 3100px page when 2480
                // was asked for. Normalise it: the marker detection is tuned to
                // a pixel scale and must not depend on the monitor.
                int height = (int)Math.Round(rendered.Height * (double)widthPixels / rendered.Width);
                Bitmap scaled = new Bitmap(widthPixels, Math.Max(1, height));
                scaled.SetResolution(300f, 300f);
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(rendered, 0, 0, scaled.Width, scaled.Height);
                }
                return scaled;
            }
        }

        public void Dispose()
        {
            // PdfDocument holds no unmanaged handle we can release explicitly;
            // the RCW is collected with this object.
        }

        // ------------------------------------------------------------------

        private static Type WinRt(string fullName)
        {
            try
            {
                return Type.GetType(fullName + ", Windows, ContentType=WindowsRuntime", false);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Calls a WinRT method and blocks until its async result is ready.</summary>
        private static object Invoke(Type owner, string method, object target, Type[] signature, object[] args)
        {
            MethodInfo info = signature == null ? owner.GetMethod(method) : owner.GetMethod(method, signature);
            if (info == null) throw new InvalidOperationException("WinRT method not found: " + owner.Name + "." + method);

            object operation = info.Invoke(target, args);
            bool hasResult = info.ReturnType.Name.StartsWith("IAsyncOperation", StringComparison.Ordinal);
            return Await(operation, info.ReturnType, hasResult);
        }

        private static object Await(object operation, Type declaredType, bool hasResult)
        {
            PropertyInfo status = AsyncInfoType.GetProperty("Status");

            // Status: 0 Started, 1 Completed, 2 Canceled, 3 Error.
            for (int spins = 0; ; spins++)
            {
                int value = (int)status.GetValue(operation, null);
                if (value == 1) break;
                if (value != 0) throw new InvalidOperationException("The Windows PDF engine failed (status " + value + ").");
                if (spins > 40000) throw new TimeoutException("The Windows PDF engine did not respond.");
                Thread.Sleep(3);
            }

            return hasResult ? declaredType.GetMethod("GetResults").Invoke(operation, null) : null;
        }
    }
}
