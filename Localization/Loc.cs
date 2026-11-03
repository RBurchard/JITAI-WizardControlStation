using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Resources;

namespace OcdWizard.ControlStation.Localization;

/// <summary>
/// Runtime-switchable string catalogue for the whole app.
///
/// XAML binds through the <c>{loc:T Key}</c> markup extension, which produces a binding to
/// this object's indexer. Changing <see cref="Culture"/> raises <c>PropertyChanged("Item[]")</c>,
/// which re-evaluates every one of those bindings so the UI switches language live, with no
/// window reload and no restart.
///
/// C# (view-models, dialogs, code-behind) calls <see cref="Get"/> / <see cref="Format"/>.
/// Note that those return a snapshot: a view-model property built from <see cref="Get"/> only
/// updates on a language switch if the view-model re-raises its own PropertyChanged. View-models
/// holding long-lived user-visible text should subscribe to <see cref="LanguageChanged"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>The app-wide instance. Bindings and <see cref="Get"/> both go through it.</summary>
    public static Loc Instance { get; } = new();

    /// <remarks>
    /// Resource base name = RootNamespace + folder + file, i.e. the compiled name of
    /// <c>Localization/Strings.resx</c>. <see cref="ResourceManager"/> handles culture fallback
    /// (de-AT → de → neutral/English) and satellite-assembly loading for us.
    /// </remarks>
    private static readonly ResourceManager Resources =
        new("OcdWizard.ControlStation.Localization.Strings", typeof(Loc).Assembly);

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    private Loc() { }

    /// <summary>Raised after the language changes, for consumers that cache resolved text.</summary>
    public event EventHandler? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Culture used to resolve keys. Setting it re-localizes the entire UI.
    /// </summary>
    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (_culture.Name == value.Name) return;
            _culture = value;
            CultureInfo.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;

            // Avalonia's indexer bindings re-read on PropertyChanged("Item") NOT on the
            // WPF "Item[]" convention, and not on the null/"" "everything changed" signal.
            // Verified empirically; using the wrong name here silently produces a UI that
            // never switches language. "Item[]" is raised too, for any WPF-style consumer.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageTag)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Two-letter tag of the active language, e.g. "de".</summary>
    public string LanguageTag => _culture.TwoLetterISOLanguageName;

    /// <summary>Binding target: <c>{loc:T SomeKey}</c> resolves to <c>Instance["SomeKey"]</c>.</summary>
    public string this[string key] => Get(key);

    /// <summary>
    /// Resolves <paramref name="key"/> in the active language. Returns the key itself when the
    /// resource is missing, which makes an untranslated string obvious on screen instead of
    /// silently rendering as blank.
    /// </summary>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        try
        {
            return Resources.GetString(key, Instance._culture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }

    /// <summary>Resolves <paramref name="key"/> and fills its <c>{0}</c>-style placeholders.</summary>
    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(Instance._culture, template, args);
        }
        catch (FormatException)
        {
            // A translation with a broken placeholder must not crash the app mid-study.
            return template;
        }
    }

    /// <summary>
    /// Applies a language by tag. <see cref="AppLanguages.SystemTag"/> (or an unknown tag)
    /// falls back to the operating-system language.
    /// </summary>
    public static void Apply(string? tag)
    {
        Instance.Culture = ResolveCulture(tag);
    }

    /// <summary>Maps a stored tag to the culture to use, honouring the OS language for "system".</summary>
    public static CultureInfo ResolveCulture(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag == AppLanguages.SystemTag)
            return SystemCulture();

        try
        {
            return CultureInfo.GetCultureInfo(tag);
        }
        catch (CultureNotFoundException)
        {
            return SystemCulture();
        }
    }

    /// <summary>
    /// The OS language if we ship it, otherwise English. Without this an unsupported OS
    /// language would resolve to the neutral resources anyway, but pinning it explicitly keeps
    /// number/date formatting consistent with the text the user actually sees.
    /// </summary>
    private static CultureInfo SystemCulture()
    {
        var os = CultureInfo.InstalledUICulture;
        var supported = AppLanguages.All.Any(l =>
            string.Equals(l.Tag, os.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
        return supported ? os : CultureInfo.GetCultureInfo("en");
    }
}
