using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OcdWizard.ControlStation.Views;

public partial class ExperimentRunnerView : UserControl
{
    public ExperimentRunnerView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
