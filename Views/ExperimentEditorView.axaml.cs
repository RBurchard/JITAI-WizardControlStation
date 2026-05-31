using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OcdWizard.ControlStation.Views;

public partial class ExperimentEditorView : UserControl
{
    public ExperimentEditorView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
