using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace OcdWizard.ControlStation.Controls;

public partial class CollapsiblePanel : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<CollapsiblePanel, string>(nameof(Title), defaultValue: "Panel");

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<CollapsiblePanel, object?>(nameof(Body));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    private Window? _popoutWindow;
    private ContentPresenter? _bodyHost;
    private TextBlock? _titleText;

    public CollapsiblePanel()
    {
        InitializeComponent();
        _bodyHost = this.FindControl<ContentPresenter>("BodyHost");
        _titleText = this.FindControl<TextBlock>("TitleText");
        TitleProperty.Changed.AddClassHandler<CollapsiblePanel>((s, _) => s.SyncTitle());
        BodyProperty.Changed.AddClassHandler<CollapsiblePanel>((s, _) => s.SyncBody());
        SyncTitle();
        SyncBody();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void SyncTitle()
    {
        if (_titleText == null) _titleText = this.FindControl<TextBlock>("TitleText");
        if (_titleText != null) _titleText.Text = Title;
    }

    private void SyncBody()
    {
        if (_bodyHost == null) _bodyHost = this.FindControl<ContentPresenter>("BodyHost");
        if (_bodyHost != null) _bodyHost.Content = Body;
    }

    private void OnPopoutClick(object? sender, RoutedEventArgs e)
    {
        if (_popoutWindow != null) { _popoutWindow.Activate(); return; }
        var content = Body;
        if (content == null) return;
        if (_bodyHost != null) _bodyHost.Content = null;

        var window = new Window
        {
            Title = Title,
            Width = 480,
            Height = 360,
            Background = new SolidColorBrush(Color.Parse("#181828")),
            DataContext = DataContext,
            Content = content
        };
        _popoutWindow = window;
        window.Closed += (_, _) =>
        {
            // Detach from the popout first; otherwise the content still has the
            // window as its parent when we try to re-host it, and Avalonia throws.
            var captured = window.Content;
            window.Content = null;
            _popoutWindow = null;
            if (_bodyHost != null) _bodyHost.Content = captured;
        };
        window.Show();
    }

    private void OnDockClick(object? sender, RoutedEventArgs e) => _popoutWindow?.Close();
}
