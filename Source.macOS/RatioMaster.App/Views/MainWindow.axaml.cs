namespace RatioMaster.App.Views;

using System.Diagnostics;
using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

using RatioMaster.App.ViewModels;
using RatioMaster.Core.Config;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType TorrentFileType = new("Torrent files")
    {
        Patterns = ["*.torrent"],
        AppleUniformTypeIdentifiers = ["org.bittorrent.torrent"],
        MimeTypes = ["application/x-bittorrent"],
    };

    private static readonly FilePickerFileType SessionFileType = new("RatioMaster sessions")
    {
        Patterns = ["*.session"],
    };

    public MainWindow()
    {
        this.InitializeComponent();
        this.AddHandler(DragDrop.DragOverEvent, this.OnDragOver);
        this.AddHandler(DragDrop.DropEvent, this.OnDrop);
    }

    private MainWindowViewModel? Model => this.DataContext as MainWindowViewModel;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ---------------- File menu ----------------

    private void OnNewTab(object? sender, EventArgs e) => this.Model?.AddTab();

    private async void OnOpenTorrent(object? sender, EventArgs e)
    {
        if (this.Model is null)
        {
            return;
        }

        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open torrent",
            AllowMultiple = true,
            FileTypeFilter = [TorrentFileType],
        });

        var first = true;
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is null)
            {
                continue;
            }

            // The first torrent lands in the current tab if it is still empty.
            if (first && this.Model.SelectedTab is { HasTorrent: false } current)
            {
                current.LoadTorrent(path);
            }
            else
            {
                this.Model.AddTab(path);
            }

            first = false;
        }
    }

    private async void OnOpenSession(object? sender, EventArgs e) => await this.OpenSessionAsync(startAfterLoad: false);

    private async void OnOpenSessionAndStart(object? sender, EventArgs e) => await this.OpenSessionAsync(startAfterLoad: true);

    private async Task OpenSessionAsync(bool startAfterLoad)
    {
        if (this.Model is null)
        {
            return;
        }

        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open session",
            AllowMultiple = false,
            FileTypeFilter = [SessionFileType],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            try
            {
                this.Model.LoadSession(path, startAfterLoad);
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
            {
                await this.ShowMessageAsync("Could not open the session", ex.Message);
            }
        }
    }

    private async void OnSaveSession(object? sender, EventArgs e)
    {
        if (this.Model is null)
        {
            return;
        }

        var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save session",
            SuggestedFileName = "ratiomaster.session",
            DefaultExtension = "session",
            FileTypeChoices = [SessionFileType],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            try
            {
                this.Model.SaveSession(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await this.ShowMessageAsync("Could not save the session", ex.Message);
            }
        }
    }

    private async void OnSaveLog(object? sender, EventArgs e)
    {
        if (this.Model?.SelectedTab is not { } tab)
        {
            return;
        }

        var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save log",
            SuggestedFileName = $"{tab.Title}.log",
            DefaultExtension = "log",
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            try
            {
                await File.WriteAllTextAsync(path, tab.GetLogText());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await this.ShowMessageAsync("Could not save the log", ex.Message);
            }
        }
    }

    private async void OnCloseTab(object? sender, EventArgs e)
    {
        if (this.Model is { SelectedTab: { } tab })
        {
            await tab.StopAsync();
            await this.Model.CloseTabAsync(tab);
        }
    }

    private async void OnRenameTab(object? sender, EventArgs e)
    {
        if (this.Model?.SelectedTab is not { } tab)
        {
            return;
        }

        var name = await TextPromptWindow.ShowAsync(this, "Rename tab", "New tab name:", tab.Title);
        if (name is { Length: > 0 })
        {
            tab.Title = name;
        }
    }

    // ---------------- Torrent menu ----------------

    private async void OnStart(object? sender, EventArgs e)
    {
        if (this.Model?.SelectedTab is { HasTorrent: true } tab)
        {
            await tab.StartAsync();
        }
    }

    private async void OnStop(object? sender, EventArgs e)
    {
        if (this.Model?.SelectedTab is { } tab)
        {
            await tab.StopAsync();
        }
    }

    private void OnAnnounceNow(object? sender, EventArgs e) =>
        this.Model?.SelectedTab?.AnnounceNowCommand.Execute(null);

    private async void OnStartAll(object? sender, EventArgs e)
    {
        if (this.Model is not null)
        {
            await this.Model.StartAllAsync();
        }
    }

    private async void OnStopAll(object? sender, EventArgs e)
    {
        if (this.Model is not null)
        {
            await this.Model.StopAllAsync();
        }
    }

    private async void OnSetUploadForAll(object? sender, EventArgs e) => await this.SetRateForAllAsync(upload: true);

    private async void OnSetDownloadForAll(object? sender, EventArgs e) => await this.SetRateForAllAsync(upload: false);

    private async Task SetRateForAllAsync(bool upload)
    {
        if (this.Model is null)
        {
            return;
        }

        var label = upload ? "upload" : "download";
        var answer = await TextPromptWindow.ShowAsync(this, $"Set {label} speed", $"New {label} speed for every tab (KiB/s):", "100");
        if (answer is null || !int.TryParse(answer, out var value) || value < 0)
        {
            return;
        }

        foreach (var tab in this.Model.Tabs)
        {
            if (upload)
            {
                tab.SetUploadRate(value);
            }
            else
            {
                tab.SetDownloadRate(value);
            }
        }
    }

    // ---------------- Settings & help ----------------

    private void OnSaveDefaults(object? sender, EventArgs e) => this.Model?.SaveSettingsFromSelectedTab();

    private void OnRevealSettings(object? sender, EventArgs e)
    {
        var directory = AppPaths.EnsureDataDirectory();
        OpenExternal(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? directory : directory);
    }

    private void OnOpenProjectPage(object? sender, EventArgs e) => OpenExternal(AppInfo.ProjectPage);

    private async void OnAbout(object? sender, EventArgs e) => await this.ShowMessageAsync(
        $"RatioMaster.NET {AppInfo.Version} for macOS",
        $"""
         A native macOS port of RatioMaster.NET.

         Emulates the tracker traffic of {Core.Emulation.ClientCatalog.AllClientNames.Count()} BitTorrent client builds
         without transferring any torrent data.

         Settings: {AppPaths.DataDirectory}
         Project:  {AppInfo.ProjectPage}
         """);

    // ---------------- Drag & drop ----------------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (this.Model is null || !e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        foreach (var item in e.Data.GetFiles() ?? [])
        {
            if (item.TryGetLocalPath() is not { } path)
            {
                continue;
            }

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".torrent":
                    if (this.Model.SelectedTab is { HasTorrent: false } empty)
                    {
                        empty.LoadTorrent(path);
                    }
                    else
                    {
                        this.Model.AddTab(path);
                    }

                    break;

                case ".session":
                    this.Model.LoadSession(path, startAfterLoad: false);
                    break;
            }
        }
    }

    // ---------------- Toolbar ----------------

    // The XAML compiler binds Button.Click strictly to EventHandler<RoutedEventArgs>
    // while NativeMenuItem.Click is a plain EventHandler, so the shared handlers
    // above need these thin adapters.
    private void OnNewTabClicked(object? sender, RoutedEventArgs e) => this.OnNewTab(sender, e);

    private void OnOpenTorrentClicked(object? sender, RoutedEventArgs e) => this.OnOpenTorrent(sender, e);

    private void OnRenameTabClicked(object? sender, RoutedEventArgs e) => this.OnRenameTab(sender, e);

    private void OnCloseTabClicked(object? sender, RoutedEventArgs e) => this.OnCloseTab(sender, e);

    private void OnStartAllClicked(object? sender, RoutedEventArgs e) => this.OnStartAll(sender, e);

    private void OnStopAllClicked(object? sender, RoutedEventArgs e) => this.OnStopAll(sender, e);

    // ---------------- Helpers ----------------

    private Task ShowMessageAsync(string title, string message) => MessageWindow.ShowAsync(this, title, message);

    private static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
