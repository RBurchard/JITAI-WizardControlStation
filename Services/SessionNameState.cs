using System;
using System.IO;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// Remembers the name of the most recently saved session, persisted as a small text file next
/// to the database. Used to detect whether the researcher left the session name untouched (still
/// the default, or still whatever was used last time) so the auto-end-of-experiment flow knows
/// when to prompt for a real name instead of silently reusing a stale one.
/// </summary>
public static class SessionNameState
{
    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OcdWizard");

    private static string FilePath => Path.Combine(Dir, "last_session_name.txt");

    public static string? LoadLastUsedName()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveLastUsedName(string name)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, name);
        }
        catch
        {
            // Non-fatal: worst case the next session starts back at the default name.
        }
    }
}
