using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OcdWizard.ControlStation.Views;

public partial class ReplayParticipantPanel : UserControl
{
    public ReplayParticipantPanel() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
