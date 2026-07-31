namespace RatioMaster.App;

using Avalonia;

internal static class Program
{
    // Avalonia must be initialised before any SynchronizationContext-dependent
    // code runs, so keep this method free of anything but the builder call.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
