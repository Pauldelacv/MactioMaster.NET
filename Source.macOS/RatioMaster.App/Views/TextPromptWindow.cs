namespace RatioMaster.App.Views;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>Single-line input sheet — the replacement for the WinForms Prompt form.</summary>
public sealed class TextPromptWindow : Window
{
    private readonly TextBox input;
    private string? result;

    private TextPromptWindow(string title, string prompt, string initialValue)
    {
        this.Title = title;
        this.Width = 420;
        this.SizeToContent = SizeToContent.Height;
        this.CanResize = false;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.ShowInTaskbar = false;

        this.input = new TextBox { Text = initialValue };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            this.result = this.input.Text ?? string.Empty;
            this.Close();
        };

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => this.Close();

        this.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = prompt, FontWeight = FontWeight.SemiBold },
                this.input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };

        this.Opened += (_, _) =>
        {
            this.input.SelectAll();
            this.input.Focus();
        };
    }

    /// <summary>Returns the entered text, or <c>null</c> when the sheet was cancelled.</summary>
    public static async Task<string?> ShowAsync(Window owner, string title, string prompt, string initialValue)
    {
        var window = new TextPromptWindow(title, prompt, initialValue);
        await window.ShowDialog(owner);
        return window.result;
    }
}
