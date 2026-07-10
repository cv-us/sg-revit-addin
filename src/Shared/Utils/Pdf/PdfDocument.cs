using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SgRevitAddin.Utils.Pdf
{
    /// <summary>
    /// Zero-dependency PDF 1.4 writer. Built-in Helvetica only (no embedding, no
    /// images, no native libs). Pure BCL — identical on net48 (Revit 2024) and
    /// net8.0-windows (Revit 2025); nothing to clash inside Revit's process.
    /// Coordinates: PDF origin is bottom-left, Y grows up. US Letter = 612x792 pt.
    /// The internal cursor (_y) walks DOWN from the top margin and auto page-breaks.
    /// </summary>
    public sealed class PdfDocument
    {
        public double PageWidth = 612, PageHeight = 792;
        public double MarginLeft = 54, MarginRight = 54, MarginTop = 54, MarginBottom = 54;

        private readonly List<byte[]> _pageStreams = new List<byte[]>();
        private StringBuilder _cur;
        private double _y;

        public PdfDocument() { NewPage(); }

        private double ContentTop => PageHeight - MarginTop;
        private double ContentBottom => MarginBottom;
        public double ContentWidth => PageWidth - MarginLeft - MarginRight;

        public void NewPage() { FlushPage(); _cur = new StringBuilder(); _y = ContentTop; }
        private void FlushPage()
        {
            if (_cur != null && _cur.Length > 0) _pageStreams.Add(Latin1(_cur.ToString()));
            _cur = null;
        }
        private void EnsureSpace(double need) { if (_y - need < ContentBottom) NewPage(); }

        // ── text primitive: absolute placement via the text matrix (Tm) ──
        public void Text(string s, double size, double x, double y, bool bold = false)
        {
            _cur.Append("BT /").Append(bold ? "F2" : "F1").Append(' ')
                .Append(Num(size)).Append(" Tf 1 0 0 1 ")
                .Append(Num(x)).Append(' ').Append(Num(y)).Append(" Tm (")
                .Append(Escape(s)).Append(") Tj ET\n");
        }

        // ── flowing helpers that advance the cursor ──
        public void Line(string s, double size = 10, bool bold = false, double indent = 0)
        {
            double lh = size * 1.4;
            EnsureSpace(lh);
            Text(s, size, MarginLeft + indent, _y - size, bold);
            _y -= lh;
        }

        public void KeyValue(string key, string value, double size = 10, double keyWidth = 130)
        {
            double lh = size * 1.4;
            EnsureSpace(lh);
            Text(key, size, MarginLeft, _y - size, bold: true);
            Text(value, size, MarginLeft + keyWidth, _y - size);
            _y -= lh;
        }

        public void Gap(double points) { EnsureSpace(points); _y -= points; }

        public void HLine(double thickness = 0.75)
        {
            EnsureSpace(thickness + 4);
            _y -= 4;
            _cur.Append(Num(thickness)).Append(" w ")
                .Append(Num(MarginLeft)).Append(' ').Append(Num(_y)).Append(" m ")
                .Append(Num(PageWidth - MarginRight)).Append(' ').Append(Num(_y)).Append(" l S\n");
        }

        private void Rect(double x, double y, double w, double h, double thickness = 0.5)
        {
            _cur.Append(Num(thickness)).Append(" w ")
                .Append(Num(x)).Append(' ').Append(Num(y)).Append(' ')
                .Append(Num(w)).Append(' ').Append(Num(h)).Append(" re S\n");
        }

        /// <summary>Bordered table; header row repeats on every page break.</summary>
        public void Table(string[] headers, double[] colWidths, IEnumerable<string[]> rows,
                          double fontSize = 9, double rowPad = 4)
        {
            double rowH = fontSize + rowPad * 2;
            double totalW = 0; foreach (var w in colWidths) totalW += w;

            void DrawRow(string[] cells, bool bold)
            {
                double bottom = _y - rowH, top = _y, x = MarginLeft;
                for (int i = 0; i < colWidths.Length; i++)
                {
                    string cell = i < cells.Length ? cells[i] : "";
                    Text(cell ?? "", fontSize, x + rowPad, bottom + rowPad + 1, bold);
                    x += colWidths[i];
                }
                Rect(MarginLeft, bottom, totalW, rowH);            // outer cell box
                x = MarginLeft;
                for (int i = 0; i < colWidths.Length - 1; i++)     // column separators
                {
                    x += colWidths[i];
                    _cur.Append("0.5 w ").Append(Num(x)).Append(' ').Append(Num(bottom))
                        .Append(" m ").Append(Num(x)).Append(' ').Append(Num(top)).Append(" l S\n");
                }
                _y = bottom;
            }

            EnsureSpace(rowH * 2);
            DrawRow(headers, bold: true);
            foreach (var r in rows)
            {
                if (_y - rowH < ContentBottom) { NewPage(); DrawRow(headers, bold: true); }
                DrawRow(r, bold: false);
            }
        }

        // ── serialize: catalog, pages, 2 fonts, then (content,page) pair per page ──
        public void Save(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                Write(fs);
        }

        public void Write(Stream stream)
        {
            FlushPage();
            var bodies = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",                                                     // 1
                null,                                                                                    // 2 Pages
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",     // 3
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",// 4
            };
            const string resources = "<< /Font << /F1 3 0 R /F2 4 0 R >> >>";

            var streamData = new List<byte[]>();
            var pageObjNums = new List<int>();
            int nextObj = bodies.Count + 1;                        // 5
            for (int i = 0; i < _pageStreams.Count; i++)
            {
                int contentObj = nextObj++, pageObj = nextObj++;
                streamData.Add(_pageStreams[i]);
                pageObjNums.Add(pageObj);
                bodies.Add("__STREAM__" + (streamData.Count - 1)); // content stream (obj = contentObj)
                bodies.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 "
                    + Num(PageWidth) + " " + Num(PageHeight) + "] /Resources " + resources
                    + " /Contents " + contentObj + " 0 R >>");
            }
            var kids = new StringBuilder();
            foreach (var n in pageObjNums) kids.Append(n).Append(" 0 R ");
            bodies[1] = "<< /Type /Pages /Count " + pageObjNums.Count
                + " /Kids [" + kids.ToString().Trim() + "] >>";

            long pos = 0;
            var offsets = new long[bodies.Count + 1];
            void WB(byte[] b) { stream.Write(b, 0, b.Length); pos += b.Length; }
            void WA(string s) { WB(Latin1(s)); }

            WA("%PDF-1.4\n%âãÏÓ\n");            // binary-comment marker
            for (int i = 0; i < bodies.Count; i++)
            {
                int objNum = i + 1;
                offsets[objNum] = pos;
                string b = bodies[i];
                if (b != null && b.StartsWith("__STREAM__", StringComparison.Ordinal))
                {
                    byte[] data = streamData[int.Parse(b.Substring(10))];
                    WA(objNum + " 0 obj\n<< /Length " + data.Length + " >>\nstream\n");
                    WB(data);
                    WA("\nendstream\nendobj\n");
                }
                else WA(objNum + " 0 obj\n" + b + "\nendobj\n");
            }

            long xref = pos;
            int size = bodies.Count + 1;
            var t = new StringBuilder();
            t.Append("xref\n0 ").Append(size).Append('\n');
            t.Append("0000000000 65535 f \n");
            for (int n = 1; n <= bodies.Count; n++)
                t.Append(offsets[n].ToString("0000000000")).Append(" 00000 n \n");
            t.Append("trailer\n<< /Size ").Append(size).Append(" /Root 1 0 R >>\nstartxref\n")
             .Append(xref).Append("\n%%EOF");
            WA(t.ToString());
        }

        // Each emitted char is ASCII/Latin1, so low-byte cast == correct encoding
        // on both TFMs (no CodePagesEncodingProvider needed on net8).
        private static byte[] Latin1(string s)
        {
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '(' || c == ')' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                // Map the common Unicode punctuation the reports use to ASCII (the
                // built-in font is ASCII-only) instead of collapsing them to '?'.
                else if (c == '→') sb.Append("->");        // →
                else if (c == '←') sb.Append("<-");        // ←
                else if (c == '≤') sb.Append("<=");        // ≤
                else if (c == '≥') sb.Append(">=");        // ≥
                else if (c == '–' || c == '—') sb.Append('-');  // – —
                else if (c == '•') sb.Append('-');         // •
                else if (c == '°') sb.Append(" deg");      // °
                else if (c == '½') sb.Append("1/2");
                else if (c == '¼') sb.Append("1/4");
                else if (c == '¾') sb.Append("3/4");
                else if (c == '‘' || c == '’') sb.Append('\'');
                else if (c == '“' || c == '”') sb.Append('"');
                else if (c < 32 || c > 126) sb.Append('?');   // last-resort for anything else
                else sb.Append(c);
            }
            return sb.ToString();
        }
        private static string Num(double v) =>
            Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
