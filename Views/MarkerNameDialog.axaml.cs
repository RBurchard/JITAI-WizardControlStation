using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OcdWizard.ControlStation.Views;

public partial class MarkerNameDialog : Window
{
    private readonly TaskCompletionSource<string?> _tcs = new();

    public MarkerNameDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Prompt for a marker name, offering <paramref name="recent"/> as quick-picks.</summary>
    public static async Task<string?> ShowAsync(Window owner, IReadOnlyList<string> recent, string initial)
    {
        var dialog = new MarkerNameDialog();
        var box = dialog.FindControl<TextBox>("NameBox")!;
        box.Text = initial;

        var picks = dialog.FindControl<WrapPanel>("QuickPicks")!;
        var picksLabel = dialog.FindControl<StackPanel>("QuickPicksLabel")!;
        if (recent.Count > 0)
        {
            picksLabel.IsVisible = true;
            foreach (var name in recent)
            {
                var btn = new Button { Content = name, Classes = { "Subtle" }, Margin = new Avalonia.Thickness(0, 0, 6, 6) };
                btn.Click += (_, _) => { box.Text = name; dialog.Confirm(); };
                picks.Children.Add(btn);
            }
        }

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
