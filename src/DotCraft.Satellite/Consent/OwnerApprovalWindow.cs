using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DotCraft.Satellite.Consent;

internal sealed class OwnerApprovalWindow : Window
{
    public OwnerApprovalWindow(RemoteToolApprovalRequest request, SatelliteStrings strings, Action<bool> decide)
    {
        Title = strings["approval.title"];
        var content = new StackPanel { Spacing = 16, Padding = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = strings.Format("approval.inviter", request.InviterName),
            TextWrapping = TextWrapping.Wrap, FontSize = 20
        });
        content.Children.Add(new TextBlock
        {
            Text = strings[request.Kind is "shell" or "terminal" or "process" or "tool"
                ? "approval.commandWarning" : "approval.fileWarning"],
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = strings["approval.operation." + request.Operation] is { Length: > 0 } label ? label : strings["approval.title"],
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock { Text = request.WorkspacePath, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 300,
            Content = new TextBlock { Text = request.Target, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var deny = new Button { Content = strings["consent.decline"] };
        var allow = new Button { Content = strings["approval.once"], Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        deny.Click += (_, _) => { decide(false); Close(); };
        allow.Click += (_, _) => { decide(true); Close(); };
        buttons.Children.Add(deny);
        buttons.Children.Add(allow);
        content.Children.Add(buttons);
        Content = new ScrollViewer
        {
            Background = (Brush)Application.Current.Resources["SatelliteSurfaceBrush"], Content = content
        };
        Closed += (_, _) => decide(false);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 540));
    }
}
