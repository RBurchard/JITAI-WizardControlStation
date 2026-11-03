using System.Collections.Generic;

namespace OcdWizard.ControlStation.Localization;

/// <summary>
/// One language the UI can be shown in.
/// </summary>
/// <param name="Tag">BCP-47 tag, e.g. "de". Must match the <c>Strings.&lt;Tag&gt;.resx</c> suffix.</param>
/// <param name="NativeName">Name in the language itself what we show in the picker, so a
/// user who cannot read the current UI language can still find their own.</param>
/// <param name="EnglishName">Name in English, for logs and tooltips.</param>
public sealed record AppLanguage(string Tag, string NativeName, string EnglishName);

/// <summary>
/// The single place that lists which languages ship with the Control Station.
///
///ADDING A LANGUAGE ──────────────────────────────────────────────────────
/// 1. Copy <c>Localization/Strings.resx</c> to <c>Localization/Strings.&lt;tag&gt;.resx</c>
///    (e.g. <c>Strings.fr.resx</c>) and translate the &lt;value&gt; elements.
/// 2. Add one line to <see cref="All"/> below.
/// That is all. Net will build the rest.
/// A fallback to english is always present. 
/// </summary>
public static class AppLanguages
{
    /// <summary>Sentinel tag meaning "follow the operating system language".</summary>
    public const string SystemTag = "system";

    /// <summary>Languages shipped with the app, in picker order.</summary>
    public static IReadOnlyList<AppLanguage> All { get; } = new[]
    {
        new AppLanguage("en", "English", "English"),
        new AppLanguage("de", "Deutsch", "German"),
        new AppLanguage("nl", "Nederlands", "Dutch"),
    };
}
