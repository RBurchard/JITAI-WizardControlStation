using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OcdWizard.ControlStation.Views;

public partial class TriviaEditorDialog : Window
{
    public TriviaEditorDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
