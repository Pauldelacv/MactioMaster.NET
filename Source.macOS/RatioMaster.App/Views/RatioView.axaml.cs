namespace RatioMaster.App.Views;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

using RatioMaster.App.ViewModels;

public partial class RatioView : UserControl
{
    private RatioTabViewModel? bound;

    public RatioView()
    {
        this.InitializeComponent();
        this.DataContextChanged += this.OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (this.bound is not null)
        {
            this.bound.LogChanged -= this.ScrollLogToEnd;
        }

        this.bound = this.DataContext as RatioTabViewModel;
        if (this.bound is not null)
        {
            this.bound.LogChanged += this.ScrollLogToEnd;
        }
    }

    private void ScrollLogToEnd()
    {
        // LogBox is the field the Avalonia name generator emits for x:Name="LogBox".
        if (this.LogBox is { } box)
        {
            // Binding updates the text after this callback returns, so move the
            // caret on the next layout pass.
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => box.CaretIndex = box.Text?.Length ?? 0,
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private async void OnBrowse(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.DataContext is not RatioTabViewModel model || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open torrent",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Torrent files")
                {
                    Patterns = ["*.torrent"],
                    AppleUniformTypeIdentifiers = ["org.bittorrent.torrent"],
                    MimeTypes = ["application/x-bittorrent"],
                },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            model.LoadTorrent(path);
        }
    }
}
