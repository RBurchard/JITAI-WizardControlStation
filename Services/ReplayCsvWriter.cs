using System.Collections.Generic;
using System.Linq;
using System.Text;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// Re-emits an imported CSV with two edits spliced in, leaving everything else byte-for-byte:
///  1. the SESSION <c>notes</c> row is rewritten from the (edited) notes text, and
///  2. a trailing <c>=== REPLAY MARKERS ===</c> section is regenerated from the current user
///     markers (any pre-existing one is removed first, so repeated saves never duplicate it).
/// </summary>
public static class ReplayCsvWriter
{
    public static string Build(ReplayData data, string notes, IReadOnlyList<UserMarker> markers)
    {
        var src = data.RawLines;

        // Locate an existing REPLAY MARKERS section [mStart, mEnd) so we can drop it.
        int mStart = -1, mEnd = src.Count;
        for (int i = 0; i < src.Count; i++)
        {
            var t = src[i].Trim();
            if (mStart < 0)
            {
                if (IsHeader(t, "REPLAY MARKERS")) mStart = i;
            }
            else if (IsAnyHeader(t)) { mEnd = i; break; }
        }

        int notesEnd = data.NotesLineStart >= 0 ? data.NotesLineStart + data.NotesLineCount : -1;

        var sb = new StringBuilder();
        bool wroteNotes = false;

        for (int i = 0; i < src.Count; i++)
        {
            if (mStart >= 0 && i >= mStart && i < mEnd) continue; // drop the old markers section

            if (data.NotesLineStart >= 0 && i == data.NotesLineStart)
            {
                sb.Append("notes,").Append(CsvUtil.Escape(notes)).Append('\n');
                wroteNotes = true;
                i = notesEnd - 1; // skip the rest of a multi-line notes record
                continue;
            }

            sb.Append(src[i]).Append('\n');

            // No notes row existed → insert one right after the SESSION header.
            if (!wroteNotes && data.NotesLineStart < 0 && i == data.SessionHeaderLine)
            {
                sb.Append("notes,").Append(CsvUtil.Escape(notes)).Append('\n');
                wroteNotes = true;
            }
        }

        var body = sb.ToString().TrimEnd('\n');

        var ordered = markers.OrderBy(m => m.Ts).ToList();
        if (ordered.Count > 0)
        {
            var section = new StringBuilder();
            section.Append("\n\n=== REPLAY MARKERS ===\n").Append("ts,label\n");
            foreach (var m in ordered)
                section.Append(m.Ts).Append(',').Append(CsvUtil.Escape(m.Label)).Append('\n');
            body += section.ToString();
        }
        else
        {
            body += "\n";
        }

        return body;
    }

    private static bool IsHeader(string trimmed, string name)
        => IsAnyHeader(trimmed) && string.Equals(trimmed.Trim('=').Trim(), name, System.StringComparison.OrdinalIgnoreCase);

    private static bool IsAnyHeader(string trimmed)
        => trimmed.Length >= 4 && trimmed.StartsWith("===") && trimmed.EndsWith("===");
}
