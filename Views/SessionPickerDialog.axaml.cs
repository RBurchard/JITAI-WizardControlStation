using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>Shows the picker and returns the single chosen session, or null if cancelled.</summary>
    public static async Task<SessionSummary?> ShowAsync(Window owner, SessionPickerViewModel vm) =>
        (await ShowManyAsync(owner, vm)).FirstOrDefault();

    /// <summary>Shows the picker and returns every chosen session; empty if cancelled.</summary>
    public static async Task<IReadOnlyList<SessionSummary>> ShowManyAsync(Window owner, SessionPickerViewModel vm)
    {
        var dialog = new SessionPickerDialog { DataContext = vm };
        // Close the window once the VM resolves (Confirm or Cancel command)
        _ = vm.ResultMany.ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close()));
        // Dismissing via the window chrome must resolve the task too, or the caller waits forever.
        dialog.Closed += (_, _) => vm.CancelCommand.Execute(null);
        await dialog.ShowDialog(owner);
        return await vm.ResultMany;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
