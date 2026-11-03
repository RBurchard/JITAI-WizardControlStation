using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OcdWizard.ControlStation.ViewModels;

namespace OcdWizard.ControlStation.Views;

public partial class MasterNotesView : UserControl
{
    public MasterNotesView()
    {
        InitializeComponent();
        // Commands are invoked from menu flyouts, which live in their own popup visual tree and
        // therefore cannot resolve $parent[Window] hand the view-model the owner directly.
        AttachedToVisualTree += (_, _) => BindOwner();
        DataContextChanged += (_, _) => BindOwner();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void BindOwner()
    {
        // Only ever set a real window DataContextChanged fires before attach, when there is none.
        if (DataContext is MasterNotesViewModel vm && TopLevel.GetTopLevel(this) is Window window)
            vm.OwnerWindow = window;
    }
}
