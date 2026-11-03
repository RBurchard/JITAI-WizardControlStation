using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OcdWizard.ControlStation.Localization;

namespace OcdWizard.ControlStation.Views;

public partial class SettingsDialog : Window
{
    // Parallel to the ListBox items: _tags[i] is the language tag for the i-th displayed row.
    private readonly List<string> _tags = new();
    private bool _initializing = true;

    public SettingsDialog()
    {
        InitializeComponent();

        var items = new List<string> { Loc.Get("Settings_LanguageSystem") };
        _tags.Add(AppLanguages.SystemTag);
        foreach (var lang in AppLanguages.All)
        {
            items.Add(lang.NativeName);
            _tags.Add(lang.Tag);
        }

        var list = this.FindControl<ListBox>("LanguageList")!;
        list.ItemsSource = items;
        list.SelectedIndex = _tags.IndexOf(LanguageSettings.Tag);
        _initializing = false;
    }

    private void OnLanguageSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (sender is not ListBox { SelectedIndex: >= 0 } list) return;
        LanguageSettings.Select(_tags[list.SelectedIndex]);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(Window owner) => new SettingsDialog().ShowDialog(owner);
}
