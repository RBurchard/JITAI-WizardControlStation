using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.Services;
using OcdWizard.ControlStation.Views;

namespace OcdWizard.ControlStation.ViewModels;

/// <summary>
/// The Master Notes table: one row per recording, assembled from the databank and from CSV files
/// shared by other people, annotated by hand, and exported to a formatted .xlsx.
///
/// It replaces the manual round-trip of exporting every session to CSV, opening them one by one
/// and re-typing the summary into a spreadsheet. Rows are persisted continuously through
/// <see cref="MasterNotesStore"/>, because this is a document a researcher fills in across many
/// sittings, not a view that is rebuilt per launch.
/// </summary>
public partial class MasterNotesViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private bool _loading;

    public ObservableCollection<MasterNoteRow> Rows { get; } = [];

    [ObservableProperty] private MasterNoteRow? _selectedRow;
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>Set by the owning Replay view-model so a row can be opened straight in the player.</summary>
    public Func<MasterNoteRow, Task>? OpenInPlayer { get; set; }

    public Window? OwnerWindow { get; set; }

    public bool HasRows => Rows.Count > 0;

    public MasterNotesViewModel(DatabaseService db)
    {
        _db = db;
        _loading = true;
        foreach (var row in MasterNotesStore.Load().OrderBy(r => r.StartedAtMs).ThenBy(r => r.Recording))
            Track(row);
        _loading = false;

        Rows.CollectionChanged += OnRowsChanged;
        StatusMessage = Rows.Count > 0 ? Loc.Format("MasterNotes_StatusLoaded", Rows.Count) : "";
    }

    // -- Adding rows -----------------------------------------------------------

    /// <summary>Pick any number of databank sessions and fold them into the table.</summary>
    [RelayCommand]
    private async Task AddFromDatabankAsync(Window? owner)
    {
        owner ??= OwnerWindow;
        if (owner == null) return;

        var picker = new SessionPickerViewModel(_db, null, multiSelect: true,
            confirmLabel: Loc.Get("MasterNotes_AddSelectedButton"));
        var picked = await SessionPickerDialog.ShowManyAsync(owner, picker);
        if (picked.Count == 0) return;

        int added = 0, refreshed = 0, failed = 0;
        foreach (var session in picked)
        {
            try
            {
                var csv = await _db.BuildSessionCsvAsync(session.Id);
                var data = ReplayCsvParser.ParseText(csv);
                var label = string.IsNullOrWhiteSpace(session.Name)
                    ? Loc.Get("Replay_SessionUnnamed")
                    : session.Name;
                if (Merge(data, label, MasterNoteSource.Databank, session.Id)) added++; else refreshed++;
            }
            catch { failed++; }
        }

        Resort();
        StatusMessage = Loc.Format("MasterNotes_StatusAdded", added, refreshed, failed);
    }

    /// <summary>Import one or more session CSV exports including ones recorded on another machine.</summary>
    [RelayCommand]
    private async Task AddFromCsvAsync(Window? owner)
    {
        owner ??= OwnerWindow;
        if (owner == null) return;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("MasterNotes_ImportCsvDialogTitle"),
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(Loc.Get("Common_FileType_SessionCsv")) { Patterns = ["*.csv"] }]
        });
        if (files.Count == 0) return;

        int added = 0, refreshed = 0, failed = 0;
        foreach (var file in files)
        {
            if (file.Path.LocalPath is not string path) { failed++; continue; }
            try
            {
                var data = ReplayCsvParser.ParseText(await File.ReadAllTextAsync(path));
                if (Merge(data, Path.GetFileName(path), MasterNoteSource.Csv, path)) added++; else refreshed++;
            }
            catch { failed++; }
        }

        Resort();
        StatusMessage = Loc.Format("MasterNotes_StatusAdded", added, refreshed, failed);
    }

    /// <summary>
    /// Adds a row for the recording, or refreshes the existing one when this source is already in
    /// the table. Returns true when a row was added.
    /// </summary>
    private bool Merge(ReplayData data, string recordingName, MasterNoteSource source, string sourceKey)
    {
        var existing = Rows.FirstOrDefault(r =>
            r.SourceKey != null && string.Equals(r.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            MasterNotesBuilder.Refresh(existing, data, recordingName);
            return false;
        }
        Track(MasterNotesBuilder.FromReplayData(data, recordingName, source, sourceKey));
        return true;
    }

    [RelayCommand]
    private void AddBlankRow()
    {
        var row = new MasterNoteRow { StartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        Track(row);
        SelectedRow = row;
        StatusMessage = Loc.Get("MasterNotes_StatusBlankAdded");
    }

    [RelayCommand]
    private void DuplicateRow(MasterNoteRow? row)
    {
        row ??= SelectedRow;
        if (row == null) return;
        var copy = new MasterNoteRow
        {
            // A duplicate is a fresh, unlinked row: it must not re-point at the original's source,
            // or the next refresh would overwrite both with the same derived values.
            Source = MasterNoteSource.Manual,
            SourceKey = null,
            StartedAtMs = row.StartedAtMs,
            Participant = row.Participant,
            Recording = row.Recording,
            DateTime = row.DateTime,
            Experiment = row.Experiment,
            VariantSource = row.VariantSource,
            AlertType = row.AlertType,
            MiniGame = row.MiniGame,
            WashDuration = row.WashDuration,
            WashSeconds = row.WashSeconds,
            InterventionNoticed = row.InterventionNoticed,
            WashingInterrupted = row.WashingInterrupted,
            Notes = row.Notes,
        };
        Rows.Insert(Math.Min(Rows.IndexOf(row) + 1, Rows.Count), copy);
        Attach(copy);
        SelectedRow = copy;
    }

    [RelayCommand]
    private void RemoveRow(MasterNoteRow? row)
    {
        row ??= SelectedRow;
        if (row == null) return;
        row.PropertyChanged -= OnRowEdited;
        Rows.Remove(row);
        StatusMessage = Loc.Get("MasterNotes_StatusRowRemoved");
    }

    [RelayCommand]
    private async Task OpenRowInPlayerAsync(MasterNoteRow? row)
    {
        row ??= SelectedRow;
        if (row == null || OpenInPlayer == null) return;
        if (row.SourceKey == null)
        {
            StatusMessage = Loc.Get("MasterNotes_StatusNoSource");
            return;
        }
        await OpenInPlayer(row);
    }

    // -- Export ----------------------------------------------------------------

    [RelayCommand]
    private async Task ExportXlsxAsync(Window? owner)
    {
        owner ??= OwnerWindow;
        if (owner == null || Rows.Count == 0) return;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Get("MasterNotes_ExportXlsxDialogTitle"),
            SuggestedFileName = $"MasterNotes_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType(Loc.Get("Common_FileType_Xlsx")) { Patterns = ["*.xlsx"] }]
        });
        if (file?.Path.LocalPath is not string path) return;

        try
        {
            XlsxWriter.Write(path, Loc.Get("MasterNotes_SheetName"), Headers, ExportRows(), ColumnWidths);
            StatusMessage = Loc.Format("MasterNotes_StatusExported", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("MasterNotes_StatusExportFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync(Window? owner)
    {
        owner ??= OwnerWindow;
        if (owner == null || Rows.Count == 0) return;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Get("MasterNotes_ExportCsvDialogTitle"),
            SuggestedFileName = $"MasterNotes_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType(Loc.Get("Common_FileType_Csv")) { Patterns = ["*.csv"] }]
        });
        if (file?.Path.LocalPath is not string path) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join(",", Headers.Select(CsvUtil.Escape)));
            foreach (var row in Rows)
                sb.AppendLine(string.Join(",", Values(row).Select(CsvUtil.Escape)));
            // UTF-8 *with* BOM: without it Excel opens the file as ANSI and mangles umlauts.
            await File.WriteAllTextAsync(path, sb.ToString(), new System.Text.UTF8Encoding(true));
            StatusMessage = Loc.Format("MasterNotes_StatusExported", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("MasterNotes_StatusExportFailed", ex.Message);
        }
    }

    /// <summary>Column headers, in sheet order. Also drives both export formats.</summary>
    public static IReadOnlyList<string> Headers =>
    [
        Loc.Get("MasterNotes_ColParticipant"),
        Loc.Get("MasterNotes_ColRecording"),
        Loc.Get("MasterNotes_ColDateTime"),
        Loc.Get("MasterNotes_ColExperiment"),
        Loc.Get("MasterNotes_ColVariantSource"),
        Loc.Get("MasterNotes_ColWashDuration"),
        Loc.Get("MasterNotes_ColWashSeconds"),
        Loc.Get("MasterNotes_ColInterventionNoticed"),
        Loc.Get("MasterNotes_ColWashingInterrupted"),
        Loc.Get("MasterNotes_ColAlertType"),
        Loc.Get("MasterNotes_ColMiniGame"),
        Loc.Get("MasterNotes_ColNotes"),
    ];

    private static readonly double[] ColumnWidths = [18, 34, 17, 20, 18, 14, 11, 24, 30, 34, 22, 80];

    private static IReadOnlyList<string> Values(MasterNoteRow r) =>
    [
        r.Participant, r.Recording, r.DateTime, r.Experiment, r.VariantSource,
        r.WashDuration, r.WashSeconds, r.InterventionNoticed, r.WashingInterrupted,
        r.AlertType, r.MiniGame, r.Notes,
    ];

    private List<IReadOnlyList<XlsxCell>> ExportRows() =>
        Rows.Select(r => (IReadOnlyList<XlsxCell>)new List<XlsxCell>
        {
            XlsxCell.Str(r.Participant),
            XlsxCell.Str(r.Recording),
            XlsxCell.Str(r.DateTime),
            XlsxCell.Str(r.Experiment),
            XlsxCell.Str(r.VariantSource),
            XlsxCell.Str(r.WashDuration),
            // Numeric where possible so the seconds column can be averaged straight in Excel.
            XlsxCell.Auto(r.WashSeconds),
            XlsxCell.Str(r.InterventionNoticed),
            XlsxCell.Str(r.WashingInterrupted),
            XlsxCell.Str(r.AlertType),
            XlsxCell.Str(r.MiniGame),
            XlsxCell.Str(r.Notes),
        }).ToList();

    // -- Persistence -----------------------------------------------------------

    private void Track(MasterNoteRow row)
    {
        Rows.Add(row);
        Attach(row);
    }

    private void Attach(MasterNoteRow row) => row.PropertyChanged += OnRowEdited;

    private void OnRowEdited(object? sender, PropertyChangedEventArgs e) => Persist();

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRows));
        Persist();
    }

    private void Persist()
    {
        if (_loading) return;
        MasterNotesStore.Save(Rows);
    }

    /// <summary>Chronological order, matching how the sheet is read.</summary>
    private void Resort()
    {
        var ordered = Rows
            .OrderBy(r => r.Participant, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.StartedAtMs)
            .ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var current = Rows.IndexOf(ordered[i]);
            if (current != i) Rows.Move(current, i);
        }
    }
}
