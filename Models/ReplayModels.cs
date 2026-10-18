using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OcdWizard.ControlStation.Models;

/// <summary>SESSION-section metadata parsed from a CSV export.</summary>
public record ReplaySession(
    string Id,
    string Name,
    string Notes,
    long? StartedAtMs,
    long? EndedAtMs,
    string ParticipantsRaw);

/// <summary>One participant's full ordered watch-data series, keyed by participant_key.</summary>
public sealed class ReplayParticipant
{
    public required string Label { get; init; }
    public required string Key { get; init; }
    public List<WatchDataSnapshot> Samples { get; } = new();
}

/// <summary>An EXPERIMENT RUN window (used to detect "experiment present" + drive the overlay).</summary>
public sealed record ReplayRun(string Name, long StartedAtMs, long? EndedAtMs);

/// <summary>
/// One INTERVENTIONS row kept in structured form. The timeline only needs a label, but the Master
/// Notes table needs the alert type and mini-game as separate fields to fill its own columns.
/// </summary>
public sealed record ReplayIntervention(
    long SentAt,
    string GameType,
    string NotificationType,
    string Response,
    long ReactionTimeMs);

public enum ReplayMarkerKind { RunStart, RunEnd, InterventionSent, Response, Note, Distraction }

/// <summary>
/// A marker auto-derived from the CSV's own sections (runs, interventions, responses, notes).
/// These are always re-derived on import and never written to REPLAY MARKERS.
/// </summary>
public sealed record ReplayMarker(long Ts, ReplayMarkerKind Kind, string Label, string? Detail = null);

/// <summary>
/// A user-placed, named annotation (e.g. "Handwashing began"). Editable and persisted to the
/// CSV's trailing <c>=== REPLAY MARKERS ===</c> section. <see cref="Ts"/> is epoch milliseconds.
/// </summary>
public partial class UserMarker : ObservableObject
{
    [ObservableProperty] private long _ts;
    [ObservableProperty] private string _label = "";

    public Guid Id { get; init; } = Guid.NewGuid();

    public UserMarker() { }
    public UserMarker(long ts, string label) { _ts = ts; _label = label; }
}

/// <summary>
/// Everything parsed from one CSV export, plus the raw lines and the located notes-row span so a
/// save can splice the notes row + REPLAY MARKERS section back without disturbing the rest.
/// </summary>
public sealed class ReplayData
{
    public required ReplaySession Session { get; init; }
    public List<ReplayParticipant> Participants { get; init; } = new();
    public List<ReplayMarker> Markers { get; init; } = new();
    public List<UserMarker> UserMarkers { get; init; } = new();
    public List<ReplayRun> Runs { get; init; } = new();
    public List<ReplayIntervention> Interventions { get; init; } = new();

    public long MinTs { get; set; }
    public long MaxTs { get; set; }

    /// <summary>Original physical lines of the file (no terminators), for save round-tripping.</summary>
    public required IReadOnlyList<string> RawLines { get; init; }

    /// <summary>Index of the SESSION <c>notes</c> row in <see cref="RawLines"/>, or -1 if absent.</summary>
    public int NotesLineStart { get; init; } = -1;

    /// <summary>Number of physical lines the notes row spans (quoted multi-line notes &gt; 1).</summary>
    public int NotesLineCount { get; init; }

    /// <summary>Index of the <c>=== SESSION ===</c> header line, or -1 (used to insert a missing notes row).</summary>
    public int SessionHeaderLine { get; init; } = -1;

    /// <summary>Watch rows left out because they had more fields than the header (a decimal-comma export).</summary>
    public int SkippedWatchRows { get; init; }

    public bool HasExperiment => Runs.Count > 0;
    public long DurationMs => Math.Max(0, MaxTs - MinTs);
}
