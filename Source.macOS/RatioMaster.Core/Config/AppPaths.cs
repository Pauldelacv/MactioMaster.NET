namespace RatioMaster.Core.Config;

using System.Runtime.InteropServices;

/// <summary>
/// Where RatioMaster keeps its files. The Windows build used
/// <c>HKCU\Software\RatioMaster.NET</c>; macOS conventions apply here instead.
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "RatioMaster.NET";

    /// <summary>
    /// <c>~/Library/Application Support/RatioMaster.NET</c> on macOS, the XDG data
    /// directory elsewhere.
    /// </summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "Logs");

    public static string EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        return DataDirectory;
    }

    private static string ResolveDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AppFolderName);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(appData, AppFolderName);
    }
}
