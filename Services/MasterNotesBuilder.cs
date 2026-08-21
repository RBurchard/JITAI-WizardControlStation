using System;
using System.Collections.Generic;
using System.Linq;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// Turns a parsed recording into a Master Notes row by filling in everything the data can answer
/// on its own, and leaving the observational columns (wash duration, whether the intervention was
/// noticed, whether washing stopped, verbatim remarks) for the researcher.
///
/// Every derived value is a best-effort read of the export, never authoritative: all of them stay
/// editable in the grid, and <see cref="Refresh"/> deliberately preserves hand-typed columns.
/// </summary>
public static class MasterNotesBuilder
{
    public static MasterNoteRow FromReplayData(
        ReplayData data, string recordingName, MasterNoteSource source, string? sourceKey)
    {
        var startedAt = data.Session.StartedAtMs ?? (data.MinTs > 0 ? data.MinTs : 0);

        var row = new MasterNoteRow
        {
            Source = source,
            SourceKey = sourceKey,
            StartedAtMs = startedAt,
            Recording = recordingName,
            Participant = Participant(data),
            DateTime = startedAt > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(startedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "",
            Experiment = Experiment(data),
            VariantSource = data.HasExperiment
                ? Loc.Get("MasterNotes_VariantLogged")
                : Loc.Get("MasterNotes_VariantNotLogged"),
            AlertType = AlertType(data),
            MiniGame = MiniGame(data),
            Notes = data.Session.Notes?.Trim() ?? "",
        };

        // A pair of user markers is the closest thing to a measured wash duration we have if the
        // researcher already annotated the recording, seed the numeric column from it.
        if (WashSeconds(data) is int seconds)
            row.WashSeconds = seconds.ToString();

        return row;
    }

    /// <summary>
    /// Re-derives the data-backed columns of an existing row in place, leaving the researcher's
    /// own columns untouched. Used when a row is re-linked to its source after the session or its
    /// notes changed.
    /// </summary>
    public static void Refresh(MasterNoteRow target, ReplayData data, string recordingName)
    {
        var fresh = FromReplayData(data, recordingName, target.Source, target.SourceKey);
        target.Participant = fresh.Participant;
        target.Recording = fresh.Recording;
        target.DateTime = fresh.DateTime;
        target.Experiment = fresh.Experiment;
        target.VariantSource = fresh.VariantSource;
        target.AlertType = fresh.AlertType;
        target.MiniGame = fresh.MiniGame;
        target.StartedAtMs = fresh.StartedAtMs;
        if (string.IsNullOrWhiteSpace(target.Notes)) target.Notes = fresh.Notes;
    }

    // -- Column derivations ----------------------------------------------------

    private static string Participant(ReplayData data)
    {
        // participant_key holds the study handle ("BauerYoYo"). participant_label is only the panel
        // slot the watch was bound to ("P1"), and older sessions left a raw row id in both none
        // of which names a person, so those fall through to the session name, which is what the
        // researcher actually typed as the handle back then.
        var names = new List<string>();
        foreach (var p in data.Participants)
        {
            var name = FirstPersonName(p.Key, p.Label);
            if (name != null && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }
        if (names.Count > 0) return string.Join(", ", names);

        var sessionName = data.Session.Name?.Trim() ?? "";
        if (sessionName.Length > 0) return sessionName;

        return FirstPersonName(data.Session.ParticipantsRaw) ?? "";
    }

    /// <summary>First candidate that names a person: not blank, not a row id, not a panel slot.</summary>
    private static string? FirstPersonName(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            var t = (c ?? "").Trim();
            if (t.Length == 0 || Guid.TryParse(t, out _) || IsSlotName(t)) continue;
            return t;
        }
        return null;
    }

    /// <summary>"P1", "P2" the participant slot, not a person.</summary>
    private static bool IsSlotName(string name) =>
        name.Length is 2 or 3 && (name[0] is 'P' or 'p') && int.TryParse(name[1..], out _);

    private static string Experiment(ReplayData data)
    {
        var names = data.Runs
            .Select(r => r.Name?.Trim() ?? "")
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count > 0 ? string.Join(", ", names) : Loc.Get("MasterNotes_ExperimentUnknown");
    }

    /// <summary>
    /// The notification types in the order they fired. A run that escalates through several types
    /// renders as "vib -> vib+sound -> annoy", matching how the sheet records it.
    /// </summary>
    private static string AlertType(ReplayData data)
    {
        var types = new List<string>();
        foreach (var i in data.Interventions.OrderBy(i => i.SentAt))
        {
            var t = i.NotificationType;
            if (t.Length == 0) continue;
            if (types.Count == 0 || !string.Equals(types[^1], t, StringComparison.OrdinalIgnoreCase))
                types.Add(t);
        }
        if (types.Count == 0) return "";
        return types.Count == 1 ? types[0] : string.Join(" -> ", types);
    }

    private static string MiniGame(ReplayData data)
    {
        var games = data.Interventions
            .OrderBy(i => i.SentAt)
            .Select(i => i.GameType)
            .Where(g => g.Length > 0 && !g.Equals("none", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (games.Count > 0) return string.Join(", ", games);
        return data.Interventions.Count > 0 ? Loc.Get("MasterNotes_MiniGameNone") : "";
    }

    /// <summary>
    /// Seconds between the first and last user marker, when at least two were placed. Anything
    /// outside a plausible hand-washing window is discarded rather than guessed at.
    /// </summary>
    private static int? WashSeconds(ReplayData data)
    {
        if (data.UserMarkers.Count < 2) return null;
        var ordered = data.UserMarkers.OrderBy(m => m.Ts).ToList();
        var span = ordered[^1].Ts - ordered[0].Ts;
        if (span <= 0 || span > 30 * 60 * 1000) return null;
        return (int)Math.Round(span / 1000.0);
    }
}
