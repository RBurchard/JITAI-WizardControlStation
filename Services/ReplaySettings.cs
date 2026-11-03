using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// Small persisted store for Replay Mode preferences, kept next to the database like
/// <see cref="FirstRunState"/>. Currently holds the last few marker names so the
/// "Add Marker" dialog can offer them as quick-picks across sessions.
/// </summary>
public static class ReplaySettings
{
    private const int MaxRecentNames = 4;

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OcdWizard");

    private static string FilePath => Path.Combine(Dir, "replay_settings.json");

    private sealed class Model
    {
        public List<string> RecentMarkerNames { get; set; } = new();
    }

    private static Model Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath)) ?? new Model();
        }
        catch { /* corrupt/missing settings are non-fatal */ }
        return new Model();
    }

    private static void Save(Model m)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(m));
        }
        catch { /* worst case the quick-picks reset */ }
    }

    /// <summary>The last <see cref="MaxRecentNames"/> marker names used, most-recent first.</summary>
    public static IReadOnlyList<string> RecentMarkerNames => Load().RecentMarkerNames;

    /// <summary>Promote a name to the front of the recent list (deduped, capped).</summary>
    public static void RememberMarkerName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0) return;
        var m = Load();
        m.RecentMarkerNames = new[] { name }
            .Concat(m.RecentMarkerNames.Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxRecentNames)
            .ToList();
        Save(m);
    }
}
