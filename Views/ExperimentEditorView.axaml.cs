using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OcdWizard.ControlStation.ViewModels;

namespace OcdWizard.ControlStation.Views;

public partial class ExperimentEditorView : UserControl
{
    public ExperimentEditorView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnExportExperimentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExperimentEditorViewModel vm && TopLevel.GetTopLevel(this) is Window owner)
            await vm.ExportExperimentCommand.ExecuteAsync(owner);
    }

    private async void OnImportExperimentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExperimentEditorViewModel vm && TopLevel.GetTopLevel(this) is Window owner)
            await vm.ImportExperimentCommand.ExecuteAsync(owner);
    }
}
