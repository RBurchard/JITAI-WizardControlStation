using System;
using System.IO;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// Tracks whether the guided tutorial has already been shown on this machine. Persisted as a
/// small marker file next to the database so the tour auto-runs exactly once on first launch;
/// the Tutorial button can always re-trigger it regardless of this flag.
/// </summary>
public static class FirstRunState
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OcdWizard");

    private static string FlagPath => Path.Combine(Dir, "tutorial_seen.flag");

    public static bool TutorialSeen => File.Exists(FlagPath);

    public static void MarkTutorialSeen()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FlagPath, DateTimeOffset.UtcNow.ToString("o"));
        }
        catch
        {
            // Non-fatal: worst case the tour offers itself again on the next launch.
        }
    }
}
