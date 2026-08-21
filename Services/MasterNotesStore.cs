using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// Persists the Master Notes table next to the database, like <see cref="ReplaySettings"/>.
///
/// The table is a working document a researcher fills in over days of testing, so it must survive
/// restarts without an explicit save. A corrupt or missing file is treated as "no rows yet" rather
/// than an error losing the annotations would be bad, but blocking startup over them is worse.
/// </summary>
public static class MasterNotesStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OcdWizard");

    private static string FilePath => Path.Combine(Dir, "master_notes.json");

    public static List<MasterNoteRow> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<MasterNoteRow>>(File.ReadAllText(FilePath), JsonOpts) ?? new();
        }
        catch { /* a corrupt table must not stop the app from opening */ }
        return new List<MasterNoteRow>();
    }

    public static void Save(IEnumerable<MasterNoteRow> rows)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            // Write-then-move so a crash mid-write cannot truncate an existing table.
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(rows, JsonOpts));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* worst case the table is rebuilt from the databank */ }
    }
}
