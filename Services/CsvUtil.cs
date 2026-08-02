using System.Collections.Generic;
using System.Text;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// CSV primitives shared by the replay parser/writer. <see cref="Escape"/> matches the rule used
/// by both exporters (<c>DatabaseService.EscapeCsv</c> and <c>PhoneCsvLogger.esc</c>) so files this
/// app writes stay byte-compatible with the originals.
/// </summary>
public static class CsvUtil
{
    /// <summary>Quote-wrap a field iff it contains a comma, quote, CR or LF; double interior quotes.</summary>
    public static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    /// <summary>
    /// Split a single CSV record into fields, honouring quoted fields and <c>""</c> escapes.
    /// Assumes the record is already on one logical line (no embedded newlines).
    /// </summary>
    public static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    /// <summary>True when <paramref name="line"/> has an odd number of unescaped quotes (so a
    /// quoted field is still open and the record continues on the next physical line).</summary>
    public static bool HasUnterminatedQuote(string line)
    {
        int count = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != '"') continue;
            if (i + 1 < line.Length && line[i + 1] == '"') { i++; continue; } // escaped pair
            count++;
        }
        return (count & 1) == 1;
    }
}
