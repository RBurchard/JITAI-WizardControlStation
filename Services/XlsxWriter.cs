using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// One cell of a sheet. A non-null <see cref="Number"/> makes Excel store the value as a real
/// number (so SUM/AVERAGE over the duration columns work), otherwise <see cref="Text"/> is
/// written as an inline string.
/// </summary>
public readonly record struct XlsxCell(string Text, double? Number = null)
{
    public static XlsxCell Str(string? text) => new(text ?? "");

    /// <summary>Number when the text parses as one, plain text otherwise (e.g. "Unknown").</summary>
    public static XlsxCell Auto(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length > 0 && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return new XlsxCell(t, d);
        return new XlsxCell(t);
    }
}

/// <summary>
/// Writes a single-sheet .xlsx straight to disk with no third-party dependency an OPC package is
/// just a zip of XML parts, and <see cref="ZipArchive"/> ships with the framework.
///
/// The output deliberately mirrors the hand-made master-notes workbook: a styled header row frozen
/// while scrolling, an auto-filter over the whole table, per-column widths and wrapped body text,
/// so the exported file is usable as-is instead of needing formatting first.
///
/// Strings are written as <c>inlineStr</c> rather than through a shared-string table: marginally
/// larger, but it keeps the writer to one pass and removes a whole class of index-mismatch bugs.
/// </summary>
public static class XlsxWriter
{
    private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // cellXfs indices baked into the Styles part below.
    private const int StyleHeader = 1;
    private const int StyleBody = 2;

    public static void Write(
        string path,
        string sheetName,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<XlsxCell>> rows,
        IReadOnlyList<double>? columnWidths = null)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        Put(zip, "[Content_Types].xml", ContentTypes);
        Put(zip, "_rels/.rels", RootRels);
        Put(zip, "xl/workbook.xml", Workbook(sheetName));
        Put(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
        Put(zip, "xl/styles.xml", Styles);
        Put(zip, "xl/worksheets/sheet1.xml", Sheet(headers, rows, columnWidths));
    }

    private static void Put(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    // -- Parts -----------------------------------------------------------------

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string WorkbookRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    /// <summary>fonts[1] = bold white header; fills[2] = dark-blue header fill; cellXfs 1/2 = header/body.</summary>
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<fonts count=\"2\">" +
        "<font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "</fonts>" +
        "<fills count=\"3\">" +
        "<fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F3864\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "</fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"3\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    private static string Workbook(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\"><sheets>" +
        "<sheet name=\"" + Escape(SanitizeSheetName(sheetName)) + "\" sheetId=\"1\" r:id=\"rId1\"/>" +
        "</sheets></workbook>";

    private static string Sheet(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<XlsxCell>> rows,
        IReadOnlyList<double>? widths)
    {
        int colCount = headers.Count;
        foreach (var r in rows) if (r.Count > colCount) colCount = r.Count;
        if (colCount == 0) colCount = 1;

        var lastCol = ColumnName(colCount);
        var lastRow = rows.Count + 1;

        var sb = new StringBuilder(4096 + rows.Count * 512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"").Append(NsMain).Append("\" xmlns:r=\"").Append(NsRel).Append("\">");
        sb.Append("<dimension ref=\"A1:").Append(lastCol).Append(lastRow).Append("\"/>");
        // Freeze the header row so the table stays readable while scrolling the long note column.
        sb.Append("<sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\">")
          .Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>")
          .Append("<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/>")
          .Append("</sheetView></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");

        if (widths is { Count: > 0 })
        {
            sb.Append("<cols>");
            for (int c = 0; c < colCount; c++)
            {
                var w = c < widths.Count ? widths[c] : 16;
                sb.Append("<col min=\"").Append(c + 1).Append("\" max=\"").Append(c + 1)
                  .Append("\" width=\"").Append(w.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append("\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }

        sb.Append("<sheetData>");
        var headerCells = new List<XlsxCell>(headers.Count);
        foreach (var h in headers) headerCells.Add(XlsxCell.Str(h));
        AppendRow(sb, 1, headerCells, StyleHeader);
        for (int i = 0; i < rows.Count; i++)
            AppendRow(sb, i + 2, rows[i], StyleBody);
        sb.Append("</sheetData>");

        // autoFilter must follow sheetData the schema fixes the element order.
        sb.Append("<autoFilter ref=\"A1:").Append(lastCol).Append(lastRow).Append("\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int rowIndex, IReadOnlyList<XlsxCell> cells, int styleId)
    {
        sb.Append("<row r=\"").Append(rowIndex).Append("\">");
        for (int c = 0; c < cells.Count; c++)
        {
            var cell = cells[c];
            var reference = ColumnName(c + 1) + rowIndex;
            if (cell.Number is double n)
            {
                sb.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(styleId).Append("\">")
                  .Append("<v>").Append(n.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
            }
            else
            {
                if (string.IsNullOrEmpty(cell.Text)) continue; // an empty cell needs no element at all
                sb.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(styleId).Append("\" t=\"inlineStr\">")
                  .Append("<is><t xml:space=\"preserve\">").Append(Escape(cell.Text)).Append("</t></is></c>");
            }
        }
        sb.Append("</row>");
    }

    // -- Primitives ------------------------------------------------------------

    /// <summary>1 becomes "A", 27 becomes "AA".</summary>
    public static string ColumnName(int index)
    {
        var sb = new StringBuilder(3);
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            index = (index - 1) / 26;
        }
        return sb.ToString();
    }

    /// <summary>Sheet names cannot exceed 31 chars or contain <c>: \ / ? * [ ]</c>.</summary>
    private static string SanitizeSheetName(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
            sb.Append(ch is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? ' ' : ch);
        var cleaned = sb.ToString().Trim();
        if (cleaned.Length == 0) cleaned = "Sheet1";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    /// <summary>
    /// XML-escapes and drops control characters, which are legal in a C# string but make the
    /// resulting workbook unopenable ("unreadable content") in Excel.
    /// </summary>
    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 16);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                case '\n':
                case '\t': sb.Append(ch); break;
                case '\r': break;
                default:
                    if (ch >= ' ') sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }
}
