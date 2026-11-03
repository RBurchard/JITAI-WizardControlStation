using System;
using System.Globalization;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OcdWizard.ControlStation.Models;

/// <summary>Where a master-notes row came from shown as a badge and used to de-duplicate adds.</summary>
public enum MasterNoteSource { Manual, Databank, Csv }

/// <summary>
/// One recording's row in the Master Notes table the per-run summary a researcher keeps beside
/// the raw data (which experiment ran, how long the participant washed, whether they noticed the
/// intervention, verbatim remarks).
///
/// Columns split into two kinds. Some are derived from the recording itself (participant, file,
/// date, experiment, alert type, mini-game) and are refreshed when the row is re-linked to its
/// source; the rest are the researcher's own observations and are only ever typed by hand. Both
/// are editable, because a derived value is a best guess and the human always overrules it.
/// </summary>
public partial class MasterNoteRow : ObservableObject
{
    /// <summary>Stable identity of the row, so edits survive a reload of the table.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Session id (databank) or full path (CSV) this row was derived from, for de-duplication.</summary>
    public string? SourceKey { get; set; }

    public MasterNoteSource Source { get; set; } = MasterNoteSource.Manual;

    // -- Derived-by-default columns ------------------------------------------

    [ObservableProperty] private string _participant = "";
    [ObservableProperty] private string _recording = "";
    [ObservableProperty] private string _dateTime = "";
    [ObservableProperty] private string _experiment = "";
    [ObservableProperty] private string _variantSource = "";
    [ObservableProperty] private string _alertType = "";
    [ObservableProperty] private string _miniGame = "";

    // -- Researcher-entered columns ------------------------------------------

    [ObservableProperty] private string _washDuration = "";
    [ObservableProperty] private string _washSeconds = "";
    [ObservableProperty] private string _interventionNoticed = "";
    [ObservableProperty] private string _washingInterrupted = "";
    [ObservableProperty] private string _notes = "";

    /// <summary>Epoch ms of the recording start the sort key; not a column.</summary>
    public long StartedAtMs { get; set; }

    [JsonIgnore]
    public string SourceBadge => Source switch
    {
        MasterNoteSource.Databank => "DB",
        MasterNoteSource.Csv => "CSV",
        _ => "—"
    };

    /// <summary>
    /// Keeps the human-readable duration in step with the numeric one typing 70 fills in
    /// "70 s (1:10)". Only overwrites text this same rule produced, so a hand-written value
    /// such as "Unknown" or "~2 min" is never clobbered.
    /// </summary>
    partial void OnWashSecondsChanging(string oldValue, string newValue)
    {
        var previousAuto = FormatWashDuration(oldValue);
        if (WashDuration.Length != 0 && WashDuration != previousAuto) return;
        var nextAuto = FormatWashDuration(newValue);
        if (nextAuto != null) WashDuration = nextAuto;
    }

    /// <summary>45 → "45 s"; 70 → "70 s (1:10)". Null when the input is not a whole number.</summary>
    public static string? FormatWashDuration(string? seconds)
    {
        if (!int.TryParse((seconds ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) || s < 0)
            return null;
        return s < 60 ? $"{s} s" : $"{s} s ({s / 60}:{s % 60:00})";
    }
}
