namespace RatioMaster.App.Views;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// Small modal message sheet. Avalonia has no built-in dialog, and a full dialog
/// library would be a heavy dependency for the three places that need one.
/// </summary>
public sealed class MessageWindow : Window
{
    private MessageWindow(string title, string message)
    {
        this.Title = title;
        this.Width = 480;
        this.SizeToContent = SizeToContent.Height;
        this.CanResize = false;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.ShowInTaskbar = false;

        var close = new Button
        {
            Content = "OK",
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 90,
        };
        close.Click += (_, _) => this.Close();

        this.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 15,
                },
                new SelectableTextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                },
                close,
            },
        };
    }

    public static Task ShowAsync(Window owner, string title, string message) =>
        new MessageWindow(title, message).ShowDialog(owner);
}
