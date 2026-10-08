namespace Compositor.Core.IO;

public static class AppPaths
{
    /// <summary>Tests and portable profiles can isolate preferences without touching the user's settings.</summary>
    public static string SettingsDirectory => Environment.GetEnvironmentVariable("COMPOSITOR_SETTINGS_DIR") is { Length: > 0 } path
        ? Path.GetFullPath(path)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CompositorWindows");
}
