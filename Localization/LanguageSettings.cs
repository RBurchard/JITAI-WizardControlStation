using System;
using System.IO;
using System.Text.Json;

namespace OcdWizard.ControlStation.Localization;

/// <summary>
/// Persists the researcher's language choice next to the database, following the same
/// pattern as <c>ReplaySettings</c> / <c>FirstRunState</c>.
///
/// The stored value is either a language tag ("de") or <see cref="AppLanguages.SystemTag"/>,
/// which is also the default a fresh install follows the operating-system language.
/// </summary>
public static class LanguageSettings
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OcdWizard");

    private static string FilePath => Path.Combine(Dir, "language_settings.json");

    private sealed class Model
    {
        public string Tag { get; set; } = AppLanguages.SystemTag;
    }

    /// <summary>The saved tag, or <see cref="AppLanguages.SystemTag"/> when nothing is saved yet.</summary>
    public static string Tag
    {
        get
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath))?.Tag
                           ?? AppLanguages.SystemTag;
            }
            catch { /* corrupt/missing settings are non-fatal fall back to the OS language */ }
            return AppLanguages.SystemTag;
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Model { Tag = value }));
            }
            catch { /* worst case the choice resets on next launch */ }
        }
    }

    /// <summary>Loads the saved language and applies it. Call once at startup, before the UI builds.</summary>
    public static void ApplySaved() => Loc.Apply(Tag);

    /// <summary>Switches language live and remembers the choice for next launch.</summary>
    public static void Select(string tag)
    {
        Tag = tag;
        Loc.Apply(tag);
    }
}
