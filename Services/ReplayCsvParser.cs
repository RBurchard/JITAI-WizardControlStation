using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// Parses a multi-section CSV export (identical format from the Control Station's
/// <c>DatabaseService.ExportToCsvAsync</c> and the phone's <c>PhoneCsvLogger</c>) into a
/// <see cref="ReplayData"/> for Replay Mode.
///
/// Robustness goals:
///  - Tolerates missing sections, <c>#</c> comment/diagnostic lines, and blank lines.
///  - Maps WATCH DATA by header name, so column reordering never shifts a value.
///  - Honours quoted, possibly multi-line CSV records in SESSION / SESSION LOGS / REPLAY MARKERS.
///  - Notes are extracted VERBATIM (only the leading <c>notes</c> key token is cut) so every
///    historical formatting variant round-trips; quote characters are intentionally preserved.
/// </summary>
public static class ReplayCsvParser
{
    private const string SECTION_SESSION = "SESSION";
    private const string SECTION_WATCH = "WATCH DATA";
    private const string SECTION_LOGS = "SESSION LOGS";
    private const string SECTION_INTERVENTIONS = "INTERVENTIONS";
    private const string SECTION_RUNS = "EXPERIMENT RUNS";
    private const string SECTION_DISTRACTIONS = "DISTRACTION RUNS";
    private const string SECTION_MARKERS = "REPLAY MARKERS";

    public static ReplayData Parse(IReadOnlyList<string> lines)
    {
        string sessionId = "", sessionName = "", participantsRaw = "";
        string notesRaw = "";
        long? startedAtMs = null, endedAtMs = null;
        int notesLineStart = -1, notesLineCount = 0, sessionHeaderLine = -1;

        var participants = new Dictionary<string, ReplayParticipant>(StringComparer.Ordinal);
        var markers = new List<ReplayMarker>();
        var userMarkers = new List<UserMarker>();
        var runs = new List<ReplayRun>();
        var interventions = new List<ReplayIntervention>();

        string section = "";
        Dictionary<string, int>? header = null;

        int i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (TryReadSectionHeader(trimmed, out var name))
            {
                section = name;
                header = null;
                if (section == SECTION_SESSION) sessionHeaderLine = i;
                i++;
                continue;
            }
            if (trimmed.Length == 0 || trimmed.StartsWith("#")) { i++; continue; }

            // Read a full logical record (may span physical lines via quoted fields).
            var (fields, raw, consumed) = ReadRecord(lines, i);

            switch (section)
            {
                case SECTION_SESSION:
                {
                    var (key, valueRaw) = SplitKeyValue(raw);
                    switch (key)
                    {
                        case "id": sessionId = Unquote(valueRaw); break;
                        case "name": sessionName = Unquote(valueRaw); break;
                        case "participants": participantsRaw = Unquote(valueRaw); break;
                        case "started_at": startedAtMs = ParseLocalTime(Unquote(valueRaw)); break;
                        case "ended_at": endedAtMs = ParseLocalTime(Unquote(valueRaw)); break;
                        case "note":
                        case "notes":
                        case "session notes":
                            // Cut ONLY the key token; decode a well-formed quoted field for clean
                            // editing, but fall back to verbatim (quotes and all) for any odd
                            // formatting so the notes can always be pulled no matter what.
                            notesRaw = DecodeNotes(valueRaw);
                            notesLineStart = i;
                            notesLineCount = consumed;
                            break;
                    }
                    break;
                }
                case SECTION_WATCH:
                    if (header == null) { header = BuildHeader(fields); break; }
                    AddWatchRow(participants, header, fields, sessionId);
                    break;
                case SECTION_LOGS:
                    if (header == null) { header = BuildHeader(fields); break; }
                    AddLogMarker(markers, header, fields);
                    break;
                case SECTION_INTERVENTIONS:
                    if (header == null) { header = BuildHeader(fields); break; }
                    AddInterventionMarkers(markers, interventions, header, fields);
                    break;
                case SECTION_RUNS:
                    if (header == null) { header = BuildHeader(fields); break; }
                    AddRun(runs, markers, header, fields);
                    break;
                case SECTION_DISTRACTIONS:
                    if (header == null) { header = BuildHeader(fields); break; }
                    AddDistractionMarker(markers, header, fields);
                    break;
                case SECTION_MARKERS:
                    if (header == null) { header = BuildHeader(fields); break; }
                    AddUserMarker(userMarkers, header, fields);
                    break;
            }

            i += consumed;
        }

        var participantList = new List<ReplayParticipant>(participants.Values);
        foreach (var p in participantList) p.Samples.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        var (minTs, maxTs) = ComputeBounds(participantList, markers, runs, userMarkers);

        return new ReplayData
        {
            Session = new ReplaySession(sessionId, sessionName, notesRaw, startedAtMs, endedAtMs, participantsRaw),
            Participants = participantList,
            Markers = markers,
            UserMarkers = userMarkers,
            Runs = runs,
            Interventions = interventions,
            MinTs = minTs,
            MaxTs = maxTs,
            RawLines = lines,
            NotesLineStart = notesLineStart,
            NotesLineCount = notesLineCount,
            SessionHeaderLine = sessionHeaderLine,
        };
    }

    public static ReplayData ParseText(string text) => Parse(SplitLines(text));

    public static List<string> SplitLines(string text)
    {
        // Normalise CRLF/CR to LF, then split. Trailing terminators are dropped per line.
        var result = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; result.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\n') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }

    // ── Section / record helpers ──────────────────────────────────────────────

    private static bool TryReadSectionHeader(string trimmed, out string name)
    {
        name = "";
        if (trimmed.Length < 4 || !trimmed.StartsWith("===") || !trimmed.EndsWith("===")) return false;
        name = trimmed.Trim('=').Trim().ToUpperInvariant();
        return name.Length > 0;
    }

    private static (List<string> fields, string raw, int consumed) ReadRecord(IReadOnlyList<string> lines, int start)
    {
        var sb = new StringBuilder(lines[start]);
        int consumed = 1;
        while (CsvUtil.HasUnterminatedQuote(sb.ToString()) && start + consumed < lines.Count)
        {
            sb.Append('\n').Append(lines[start + consumed]);
            consumed++;
        }
        var raw = sb.ToString();
        return (CsvUtil.SplitCsv(raw), raw, consumed);
    }

    private static Dictionary<string, int> BuildHeader(List<string> fields)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int c = 0; c < fields.Count; c++)
        {
            var key = fields[c].Trim();
            if (key.Length > 0 && !map.ContainsKey(key)) map[key] = c;
        }
        return map;
    }

    private static string Get(Dictionary<string, int> header, List<string> fields, string col)
        => header.TryGetValue(col, out var idx) && idx >= 0 && idx < fields.Count ? fields[idx] : "";

    /// <summary>Split a SESSION row into (lowercased key, verbatim remainder-after-first-comma).</summary>
    private static (string key, string value) SplitKeyValue(string raw)
    {
        int comma = raw.IndexOf(',');
        if (comma < 0) return (Unquote(raw).Trim().ToLowerInvariant(), "");
        var key = Unquote(raw[..comma]).Trim().ToLowerInvariant();
        var value = raw[(comma + 1)..];
        return (key, value);
    }

    // ── Row mappers ───────────────────────────────────────────────────────────

    private static void AddWatchRow(Dictionary<string, ReplayParticipant> participants,
        Dictionary<string, int> h, List<string> f, string sessionId)
    {
        var label = Get(h, f, "participant_label");
        var key = Get(h, f, "participant_key");
        if (string.IsNullOrEmpty(key)) key = string.IsNullOrEmpty(label) ? "P1" : label;
        if (!participants.TryGetValue(key, out var p))
        {
            p = new ReplayParticipant { Label = string.IsNullOrEmpty(label) ? key : label, Key = key };
            participants[key] = p;
        }

        var ts = ParseLong(Get(h, f, "timestamp"));
        if (ts == 0) return; // a row without a timestamp is unusable for the timeline

        p.Samples.Add(new WatchDataSnapshot(
            Timestamp: ts,
            HeartRate: ParseFloat(Get(h, f, "heart_rate")),
            AccelX: ParseFloat(Get(h, f, "accel_x")),
            AccelY: ParseFloat(Get(h, f, "accel_y")),
            AccelZ: ParseFloat(Get(h, f, "accel_z")),
            StepCount: (int)ParseLong(Get(h, f, "step_count")),
            SessionId: sessionId,
            GyroX: ParseFloat(Get(h, f, "gyro_x")),
            GyroY: ParseFloat(Get(h, f, "gyro_y")),
            GyroZ: ParseFloat(Get(h, f, "gyro_z")),
            RotationX: ParseFloat(Get(h, f, "rot_x")),
            RotationY: ParseFloat(Get(h, f, "rot_y")),
            RotationZ: ParseFloat(Get(h, f, "rot_z")),
            RotationW: ParseFloat(Get(h, f, "rot_w")),
            Barometer: ParseFloat(Get(h, f, "baro_hpa")),
            Light: ParseFloat(Get(h, f, "light_lux"))));
    }

    private static void AddLogMarker(List<ReplayMarker> markers, Dictionary<string, int> h, List<string> f)
    {
        var kind = Get(h, f, "kind");
        if (!string.Equals(kind, "Note", StringComparison.OrdinalIgnoreCase)) return;
        var ts = ParseLong(Get(h, f, "ts"));
        if (ts == 0) return;
        var value = ExtractJsonString(Get(h, f, "payload_json"), "value");
        markers.Add(new ReplayMarker(ts, ReplayMarkerKind.Note, Loc.Get("Replay_MarkerLabel_Note"),
            string.IsNullOrWhiteSpace(value) ? null : value));
    }

    private static void AddInterventionMarkers(List<ReplayMarker> markers, List<ReplayIntervention> interventions,
        Dictionary<string, int> h, List<string> f)
    {
        var sentAt = ParseLong(Get(h, f, "sent_at"));
        if (sentAt == 0) return;
        var game = Get(h, f, "game_type");
        var notif = Get(h, f, "notification_type");
        interventions.Add(new ReplayIntervention(sentAt, game.Trim(), notif.Trim(),
            Get(h, f, "response").Trim(), ParseLong(Get(h, f, "reaction_time_ms"))));
        var label = !string.IsNullOrEmpty(game) ? game : (!string.IsNullOrEmpty(notif) ? notif : Loc.Get("Replay_MarkerLabel_Intervention"));
        markers.Add(new ReplayMarker(sentAt, ReplayMarkerKind.InterventionSent, Loc.Format("Replay_MarkerLabel_InterventionSent", label)));

        var response = Get(h, f, "response");
        if (!string.IsNullOrEmpty(response))
        {
            var rt = ParseLong(Get(h, f, "reaction_time_ms"));
            var at = rt > 0 ? sentAt + rt : sentAt;
            var detail = rt > 0
                ? Loc.Format("Replay_MarkerDetail_ResponseWithReactionTime", response, (rt / 1000.0).ToString("0.0", Loc.Instance.Culture))
                : response;
            markers.Add(new ReplayMarker(at, ReplayMarkerKind.Response, Loc.Format("Replay_MarkerLabel_Response", response), detail));
        }
    }

    private static void AddRun(List<ReplayRun> runs, List<ReplayMarker> markers, Dictionary<string, int> h, List<string> f)
    {
        var start = ParseLong(Get(h, f, "started_at"));
        if (start == 0) return;
        var endRaw = Get(h, f, "ended_at");
        long? end = ParseLong(endRaw) is > 0 and var e ? e : null;
        var nm = Get(h, f, "experiment_name");
        if (string.IsNullOrWhiteSpace(nm)) nm = Loc.Get("Replay_RunFallbackName");
        runs.Add(new ReplayRun(nm, start, end));
        markers.Add(new ReplayMarker(start, ReplayMarkerKind.RunStart, Loc.Format("Replay_MarkerLabel_RunStart", nm)));
        if (end is long ee) markers.Add(new ReplayMarker(ee, ReplayMarkerKind.RunEnd, Loc.Format("Replay_MarkerLabel_RunEnd", nm)));
    }

    private static void AddDistractionMarker(List<ReplayMarker> markers, Dictionary<string, int> h, List<string> f)
    {
        var start = ParseLong(Get(h, f, "started_at"));
        if (start == 0) return;
        var response = Get(h, f, "response");
        markers.Add(new ReplayMarker(start, ReplayMarkerKind.Distraction, Loc.Get("Replay_MarkerLabel_Distraction"),
            string.IsNullOrEmpty(response) ? null : response));
    }

    private static void AddUserMarker(List<UserMarker> userMarkers, Dictionary<string, int> h, List<string> f)
    {
        var ts = ParseLong(Get(h, f, "ts"));
        if (ts == 0) return;
        var label = Get(h, f, "label");
        userMarkers.Add(new UserMarker(ts, label));
    }

    // ── Primitives ────────────────────────────────────────────────────────────

    private static (long min, long max) ComputeBounds(
        List<ReplayParticipant> participants, List<ReplayMarker> markers, List<ReplayRun> runs, List<UserMarker> userMarkers)
    {
        long min = long.MaxValue, max = long.MinValue;
        void Acc(long t) { if (t < min) min = t; if (t > max) max = t; }

        foreach (var p in participants)
            if (p.Samples.Count > 0) { Acc(p.Samples[0].Timestamp); Acc(p.Samples[^1].Timestamp); }
        foreach (var m in markers) Acc(m.Ts);
        foreach (var r in runs) { Acc(r.StartedAtMs); if (r.EndedAtMs is long e) Acc(e); }
        foreach (var u in userMarkers) Acc(u.Ts);

        if (min == long.MaxValue) return (0, 0);
        return (min, max);
    }

    private static long ParseLong(string s)
        => long.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static float ParseFloat(string s)
        => float.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

    private static long? ParseLocalTime(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Equals("active", StringComparison.OrdinalIgnoreCase)) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        return null;
    }

    /// <summary>
    /// Decode the notes value found after the <c>notes,</c> key. A single well-formed quoted field
    /// (including multi-line) is unwrapped to clean text; anything else plain text, stray quotes,
    /// odd legacy formatting is returned verbatim so extraction never fails.
    /// </summary>
    private static string DecodeNotes(string valueRaw)
    {
        var trimmed = valueRaw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' && !CsvUtil.HasUnterminatedQuote(trimmed))
            return trimmed[1..^1].Replace("\"\"", "\"");
        return valueRaw;
    }

    private static string Unquote(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
            return v[1..^1].Replace("\"\"", "\"");
        return v;
    }

    private static string ExtractJsonString(string payloadCsvField, string property)
    {
        var json = Unquote(payloadCsvField);
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(property, out var el) &&
                el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? "";
        }
        catch { /* non-JSON payloads are fine; just no detail */ }
        return "";
    }
}
