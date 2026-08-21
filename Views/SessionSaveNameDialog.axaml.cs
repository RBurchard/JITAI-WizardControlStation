using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OcdWizard.ControlStation.Views;

/// <summary>
/// Prompts for a session name when the experiment auto-ends the session and the name is still
/// the default (or unchanged from last time) — so a researcher who forgot to rename it gets one
/// last chance before the session becomes hard to tell apart from others in the picker/export.
/// </summary>
public partial class SessionSaveNameDialog : Window
{
    private readonly TaskCompletionSource<string?> _tcs = new();

    public SessionSaveNameDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public static async Task<string?> ShowAsync(Window owner, string initial)
    {
        var dialog = new SessionSaveNameDialog();
        var box = dialog.FindControl<TextBox>("NameBox")!;
        box.Text = initial;

        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await dialog.ShowDialog(owner);
        return await dialog._tcs.Task;
    }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Confirm(); }
        else if (e.Key == Key.Escape) { e.Handled = true; Cancel(); }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Confirm();
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Cancel();

    private void Confirm()
    {
        var text = this.FindControl<TextBox>("NameBox")?.Text?.Trim();
        _tcs.TrySetResult(string.IsNullOrEmpty(text) ? null : text);
        Close();
    }

    private void Cancel()
    {
        _tcs.TrySetResult(null);
        Close();
    }
}
