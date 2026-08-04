using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OcdWizard.ControlStation.Localization;

namespace OcdWizard.ControlStation.Views;

public partial class SaveTargetDialog : Window
{
    public enum Result { Overwrite, SaveAs, Cancel }

    private readonly TaskCompletionSource<Result> _tcs = new();

    public SaveTargetDialog()
    {
        InitializeComponent();
        // Closing via the window chrome counts as Cancel.
        Closed += (_, _) => _tcs.TrySetResult(Result.Cancel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public static async Task<Result> ShowAsync(Window owner, string fileName)
    {
        var dialog = new SaveTargetDialog();
        if (!string.IsNullOrEmpty(fileName) && dialog.FindControl<TextBlock>("Message") is { } msg)
            msg.Text = Loc.Format("SaveTarget_MessageWithFileFormat", fileName);
        await dialog.ShowDialog(owner);
        return await dialog._tcs.Task;
    }

    private void OnOverwriteClick(object? sender, RoutedEventArgs e) => Finish(Result.Overwrite);
    private void OnSaveAsClick(object? sender, RoutedEventArgs e) => Finish(Result.SaveAs);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Finish(Result.Cancel);

    private void Finish(Result r)
    {
        _tcs.TrySetResult(r);
        Close();
    }
}
