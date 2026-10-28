using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Compositor.Desktop;

internal static class BatchShortcut
{
    [SupportedOSPlatform("windows")]
    internal static void Create(string path, string recipePath)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
        var arguments = "--batch-recipe " + Quote(recipePath);
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            arguments = Quote(Assembly.GetEntryAssembly()!.Location) + " " + arguments;
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcuts are unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        object? shortcut = null;
        try
        {
            shortcut = shell.CreateShortcut(path);
            dynamic link = shortcut;
            link.TargetPath = executable;
            link.Arguments = arguments;
            link.WorkingDirectory = AppContext.BaseDirectory;
            link.Description = Localize.Text("Drop images or folders here to run this batch.");
            link.Save();
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
