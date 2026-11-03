using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace OcdWizard.ControlStation.Localization;

/// <summary>
/// XAML markup extension that resolves a localized string and keeps tracking the active language.
///
/// Usage add <c>xmlns:loc="using:OcdWizard.ControlStation.Localization"</c> to the root element,
/// then:
/// <code>
///   &lt;TextBlock Text="{loc:T Runner_StartButton}" /&gt;
///   &lt;Button Content="{loc:T Common_Cancel}" ToolTip.Tip="{loc:T Runner_StartTooltip}" /&gt;
/// </code>
///
/// It deliberately returns a <see cref="Binding"/> rather than a plain string: a string would be
/// resolved once at load time and freeze in whatever language was active then. Binding to
/// <see cref="Loc"/>'s indexer means the <c>PropertyChanged("Item[]")</c> raised on a language
/// switch re-reads every localized property in the live visual tree.
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    /// <summary>Resource key, matching a &lt;data name="..."&gt; entry in Strings.resx.</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };
}
