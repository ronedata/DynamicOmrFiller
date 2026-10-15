using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace DynamicOmrFiller
{
    /// <summary>
    /// User defaults persisted between runs, stored as XML under
    /// %APPDATA%\DynamicOmrFiller\settings.xml.
    ///
    /// Saving is explicit: the user presses "Save as default" in a card, which
    /// stamps that group only. The Has* flags say whether a group has ever been
    /// saved, so an untouched install still starts with the built-in samples.
    /// A missing or corrupt file must never stop the app - Load falls back to
    /// plain defaults instead of throwing.
    /// </summary>
    internal sealed class AppSettings
    {
        public string RollNumbers { get; set; }
        public string RegistrationNumbers { get; set; }
        public bool WriteStudentNumbers { get; set; }
        public float WriteOffsetRows { get; set; }
        public bool HasStudentDefaults { get; set; }

        public string SaqText { get; set; }
        public int SaqFontSizePixels { get; set; }
        public int SaqLabelFontSizePixels { get; set; }
        public StringAlignment SaqAlignHorizontal { get; set; }
        public StringAlignment SaqAlignVertical { get; set; }
        public bool HasSaqDefaults { get; set; }

        public AppSettings()
        {
            SaqAlignHorizontal = StringAlignment.Center;
            SaqAlignVertical = StringAlignment.Center;
            WriteStudentNumbers = true;
            WriteOffsetRows = 1.0f;
        }

        public static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DynamicOmrFiller");
                return Path.Combine(dir, "settings.xml");
            }
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();

            try
            {
                if (!File.Exists(FilePath)) return settings;

                XElement root = XDocument.Load(FilePath).Root;
                if (root == null) return settings;

                XElement student = root.Element("student");
                if (student != null)
                {
                    settings.RollNumbers = NormalizeNewLines((string)student.Element("rollNumbers"));
                    settings.RegistrationNumbers = NormalizeNewLines((string)student.Element("registrationNumbers"));
                    // Missing in files written before 1.8.0 - default it on.
                    settings.WriteStudentNumbers = ReadBool(student.Element("writeNumbers"), true);
                    settings.WriteOffsetRows = ReadFloat(student.Element("writeOffsetRows"), 1.0f);
                    settings.HasStudentDefaults = true;
                }

                XElement saq = root.Element("saq");
                if (saq != null)
                {
                    settings.SaqText = NormalizeNewLines((string)saq.Element("text"));
                    settings.SaqFontSizePixels = ReadInt(saq.Element("fontSize"), 0);
                    settings.SaqLabelFontSizePixels = ReadInt(saq.Element("labelFontSize"), 0);
                    settings.SaqAlignHorizontal = ReadAlignment(saq.Element("alignHorizontal"), StringAlignment.Center);
                    settings.SaqAlignVertical = ReadAlignment(saq.Element("alignVertical"), StringAlignment.Center);
                    settings.HasSaqDefaults = true;
                }
            }
            catch
            {
                // A damaged settings file should not block startup.
                return new AppSettings();
            }

            return settings;
        }

        public void Save()
        {
            string directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            XElement root = new XElement("dynamicOmrFiller");

            if (HasStudentDefaults)
            {
                root.Add(new XElement("student",
                    new XElement("rollNumbers", RollNumbers ?? string.Empty),
                    new XElement("registrationNumbers", RegistrationNumbers ?? string.Empty),
                    new XElement("writeNumbers", WriteStudentNumbers),
                    new XElement("writeOffsetRows", WriteOffsetRows)));
            }

            if (HasSaqDefaults)
            {
                root.Add(new XElement("saq",
                    new XElement("text", SaqText ?? string.Empty),
                    new XElement("fontSize", SaqFontSizePixels),
                    new XElement("labelFontSize", SaqLabelFontSizePixels),
                    new XElement("alignHorizontal", SaqAlignHorizontal),
                    new XElement("alignVertical", SaqAlignVertical)));
            }

            new XDocument(root).Save(FilePath);
        }

        /// <summary>
        /// XML collapses CRLF to LF on read, which a multiline TextBox renders
        /// as one run-on line, so put Windows line endings back.
        /// </summary>
        private static string NormalizeNewLines(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }

        private static float ReadFloat(XElement element, float fallback)
        {
            float parsed;
            if (element == null) return fallback;
            return float.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static bool ReadBool(XElement element, bool fallback)
        {
            bool parsed;
            if (element == null) return fallback;
            return bool.TryParse(element.Value, out parsed) ? parsed : fallback;
        }

        private static int ReadInt(XElement element, int fallback)
        {
            int parsed;
            if (element == null) return fallback;
            return int.TryParse(element.Value, out parsed) ? parsed : fallback;
        }

        private static StringAlignment ReadAlignment(XElement element, StringAlignment fallback)
        {
            if (element == null) return fallback;
            try
            {
                return (StringAlignment)Enum.Parse(typeof(StringAlignment), element.Value, true);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
