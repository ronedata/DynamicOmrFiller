using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DynamicOmrFiller
{
    public static class ExcelLoader
    {
        private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        public static List<Dictionary<string, string>> LoadRows(string filePath)
        {
            List<string> ignored;
            return LoadRows(filePath, out ignored);
        }

        /// <summary>
        /// As above, and reports the column headers in sheet order - a
        /// Dictionary does not promise to keep insertion order, so the column
        /// pickers need the list separately.
        /// </summary>
        public static List<Dictionary<string, string>> LoadRows(string filePath, out List<string> headers)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("File not found.", filePath);

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".csv") return LoadCsv(filePath, out headers);
            if (ext == ".xlsx") return LoadXlsx(filePath, out headers);
            throw new InvalidOperationException("Unsupported file type. Use .xlsx or .csv.");
        }

        private static List<Dictionary<string, string>> LoadCsv(string filePath, out List<string> headers)
        {
            string[] lines = File.ReadAllLines(filePath);
            List<List<string>> grid = new List<List<string>>();
            foreach (string line in lines)
            {
                grid.Add(SplitCsvLine(line));
            }
            return BuildRows(grid, out headers);
        }

        private static List<Dictionary<string, string>> LoadXlsx(string filePath, out List<string> headers)
        {
            string[] sharedStrings;
            List<List<string>> grid;

            using (ZipArchive zip = ZipFile.OpenRead(filePath))
            {
                sharedStrings = ReadSharedStrings(zip);

                ZipArchiveEntry sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml")
                    ?? zip.Entries.FirstOrDefault(entry =>
                        entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                        && entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

                if (sheetEntry == null) throw new InvalidOperationException("Worksheet not found inside the .xlsx file.");

                grid = ReadSheet(sheetEntry, sharedStrings);
            }

            return BuildRows(grid, out headers);
        }

        private static List<Dictionary<string, string>> BuildRows(List<List<string>> grid, out List<string> headers)
        {
            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
            headers = new List<string>();
            if (grid.Count == 0) return rows;

            List<string> headerCells = grid[0];
            foreach (string cell in headerCells)
            {
                string header = (cell ?? string.Empty).Trim();
                if (header.Length == 0) continue;
                if (headers.Any(existing => string.Equals(existing, header, StringComparison.OrdinalIgnoreCase))) continue;
                headers.Add(header);
            }

            for (int r = 1; r < grid.Count; r++)
            {
                List<string> cells = grid[r];
                if (cells.All(string.IsNullOrWhiteSpace)) continue;

                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < headerCells.Count && c < cells.Count; c++)
                {
                    string header = (headerCells[c] ?? string.Empty).Trim();
                    if (header.Length == 0 || row.ContainsKey(header)) continue;
                    row[header] = cells[c] ?? string.Empty;
                }
                rows.Add(row);
            }
            return rows;
        }

        private static string[] ReadSharedStrings(ZipArchive zip)
        {
            ZipArchiveEntry entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return new string[0];
            using (Stream stream = entry.Open())
            {
                XDocument doc = XDocument.Load(stream);
                return doc.Descendants(Ns + "si")
                    .Select(si => string.Concat(si.Descendants(Ns + "t").Select(t => t.Value)))
                    .ToArray();
            }
        }

        private static List<List<string>> ReadSheet(ZipArchiveEntry sheetEntry, string[] sharedStrings)
        {
            List<List<string>> rows = new List<List<string>>();
            using (Stream stream = sheetEntry.Open())
            {
                XDocument doc = XDocument.Load(stream);
                foreach (XElement rowElem in doc.Descendants(Ns + "row"))
                {
                    List<string> cells = new List<string>();
                    foreach (XElement cellElem in rowElem.Elements(Ns + "c"))
                    {
                        int colIndex = ColumnRefToIndex(cellElem.Attribute("r")?.Value);
                        while (cells.Count < colIndex)
                        {
                            cells.Add(string.Empty);
                        }

                        string type = cellElem.Attribute("t")?.Value;
                        string value;

                        if (type == "s")
                        {
                            string idxStr = cellElem.Element(Ns + "v")?.Value;
                            int idx;
                            value = (int.TryParse(idxStr, out idx) && idx >= 0 && idx < sharedStrings.Length)
                                ? sharedStrings[idx]
                                : string.Empty;
                        }
                        else if (type == "inlineStr")
                        {
                            XElement inlineStr = cellElem.Element(Ns + "is");
                            value = inlineStr != null
                                ? string.Concat(inlineStr.Descendants(Ns + "t").Select(t => t.Value))
                                : string.Empty;
                        }
                        else
                        {
                            value = cellElem.Element(Ns + "v")?.Value ?? string.Empty;
                        }

                        cells.Add(value);
                    }
                    rows.Add(cells);
                }
            }
            return rows;
        }

        private static int ColumnRefToIndex(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return 0;
            int col = 0;
            foreach (char c in cellRef)
            {
                char up = char.ToUpperInvariant(c);
                if (up < 'A' || up > 'Z') break;
                col = col * 26 + (up - 'A' + 1);
            }
            return Math.Max(0, col - 1);
        }

        private static List<string> SplitCsvLine(string line)
        {
            List<string> fields = new List<string>();
            if (line == null) return fields;

            bool inQuote = false;
            StringBuilder current = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuote = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuote = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(current.ToString().Trim());
                        current.Length = 0;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }
            fields.Add(current.ToString().Trim());
            return fields;
        }
    }
}
