using Avalonia.Controls;
using Avalonia.Interactivity;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.ViewModels;

namespace OcdWizard.ControlStation.Views;

public partial class SessionPickerDialog : Window
{
    public SessionPickerDialog()
    {
        InitializeComponent();
    }

    public static async System.Threading.Tasks.Task<SessionSummary?> ShowAsync(
        Window owner, SessionPickerViewModel vm)
    {
        var dialog = new SessionPickerDialog { DataContext = vm };
        // Close the window once the VM resolves (Confirm or Cancel command)
        _ = vm.Result.ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close()));
        await dialog.ShowDialog(owner);
        return await vm.Result;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
