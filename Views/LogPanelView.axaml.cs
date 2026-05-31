using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OcdWizard.ControlStation.Views;

public partial class LogPanelView : UserControl
{
    public LogPanelView() { InitializeComponent(); }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
