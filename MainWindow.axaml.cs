using Avalonia.Controls;
using Avalonia.Interactivity;
using OcdWizard.ControlStation.ViewModels;
using OcdWizard.ControlStation.Views;

namespace OcdWizard.ControlStation;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private async void OnManageTriviaClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.OpenTriviaEditorCommand.ExecuteAsync(this);
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.ExportCommand.ExecuteAsync(this);
    }
}
