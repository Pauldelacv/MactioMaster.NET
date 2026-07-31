namespace RatioMaster.App.ViewModels;

using System.Collections.ObjectModel;

using RatioMaster.Core.Config;

public sealed class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private RatioTabViewModel? selectedTab;
    private int tabCounter;

    public MainWindowViewModel()
    {
        this.Settings = AppSettings.Load();
        this.AddTab();
    }

    public AppSettings Settings { get; }

    public ObservableCollection<RatioTabViewModel> Tabs { get; } = [];

    public RatioTabViewModel? SelectedTab
    {
        get => this.selectedTab;
        set
        {
            if (this.SetField(ref this.selectedTab, value))
            {
                this.Raise(nameof(this.HasSelection));
            }
        }
    }

    public bool HasSelection => this.selectedTab is not null;

    public string WindowTitle => $"RatioMaster.NET {AppInfo.Version} for macOS";

    public RatioTabViewModel AddTab(string? torrentPath = null)
    {
        var tab = new RatioTabViewModel(this.Settings) { Title = $"RM {++this.tabCounter}" };
        this.Tabs.Add(tab);
        this.SelectedTab = tab;

        if (torrentPath is { Length: > 0 })
        {
            tab.LoadTorrent(torrentPath);
        }

        return tab;
    }

    public async Task CloseTabAsync(RatioTabViewModel tab)
    {
        if (this.Tabs.Count <= 1)
        {
            return;
        }

        var index = this.Tabs.IndexOf(tab);
        this.Tabs.Remove(tab);
        await tab.DisposeAsync().ConfigureAwait(true);
        this.SelectedTab = this.Tabs.Count == 0 ? null : this.Tabs[Math.Clamp(index, 0, this.Tabs.Count - 1)];
    }

    public async Task StartAllAsync()
    {
        foreach (var tab in this.Tabs.ToList())
        {
            await tab.StartAsync().ConfigureAwait(true);
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var tab in this.Tabs.ToList())
        {
            await tab.StopAsync().ConfigureAwait(true);
        }
    }

    public void SaveSession(string path) =>
        SessionDocument.Save(path, this.Tabs.Select(tab => new SessionEntry(tab.Title, tab.Options)));

    public void LoadSession(string path, bool startAfterLoad)
    {
        var entries = SessionDocument.Load(path);
        if (entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            var tab = this.AddTab();
            tab.Title = entry.Name.Length > 0 ? entry.Name : tab.Title;
            tab.ApplyOptions(entry.Options);

            if (startAfterLoad && tab.HasTorrent)
            {
                _ = tab.StartAsync();
            }
        }
    }

    public void SaveSettingsFromSelectedTab()
    {
        this.selectedTab?.ApplyToSettings(this.Settings);
        this.Settings.Save();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tab in this.Tabs.ToList())
        {
            await tab.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public static class AppInfo
{
    public const string Version = "0.44";

    public const string ProjectPage = "https://github.com/NikolayIT/RatioMaster.NET";
}
