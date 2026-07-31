namespace RatioMaster.App;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using RatioMaster.App.ViewModels;
using RatioMaster.App.Views;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            var window = new MainWindow { DataContext = viewModel };

            // Announcing "stopped" to every tracker is the polite exit; give it a
            // moment before the process goes away.
            desktop.ShutdownRequested += async (_, e) =>
            {
                if (viewModel.Tabs.Any(tab => tab.IsRunning))
                {
                    e.Cancel = true;
                    await viewModel.StopAllAsync();
                    viewModel.SaveSettingsFromSelectedTab();
                    await viewModel.DisposeAsync();
                    desktop.Shutdown();
                }
                else
                {
                    viewModel.SaveSettingsFromSelectedTab();
                }
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
