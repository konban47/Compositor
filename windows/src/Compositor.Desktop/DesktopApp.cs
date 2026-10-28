using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Compositor.Desktop;

public sealed class DesktopApp : Application
{
    public override void Initialize()
    {
        // The Mac build asks for the dark appearance and draws its own greys on top of it
        // (ContentView's `.preferredColorScheme(.dark)`), so the port does the same rather than
        // following whatever the machine is set to — a light menu bar over a dark canvas was
        // unreadable, and the Mac has no light appearance to match.
        RequestedThemeVariant = EditorPreferences.Current.Theme >= 2 ? ThemeVariant.Light : ThemeVariant.Dark;
        _theme = new FluentTheme();
        Styles.Add(_theme);
        EditorTheme.Apply(Styles);
    }

    private FluentTheme? _theme;
    internal void RefreshAppearance()
    {
        Skin.RefreshPalette();
        RequestedThemeVariant = EditorPreferences.Current.Theme >= 2 ? ThemeVariant.Light : ThemeVariant.Dark;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) foreach (var window in desktop.Windows) WindowChrome.ApplyNative(window);
        if (_theme is { } theme) Skin.Apply(theme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // The palette is moved onto the Mac's once the theme is attached: until then its resources have not
        // been read off its own XAML, and a dictionary with nothing in it has nothing to move.
        if (_theme is { } theme) Skin.Apply(theme);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;
            window.Opened += async (_, _) =>
            {
                if (desktop.Args is ["--performance-checks", var input, var report])
                {
                    try { await window.PerformanceCheck(input, report); desktop.Shutdown(); }
                    catch (Exception error) { Console.Error.WriteLine(error); desktop.Shutdown(1); }
                    return;
                }
                if (desktop.Args is ["--batch-recipe", var recipe, .. var inputs])
                {
                    await window.RunBatchRecipe(recipe, inputs);
                    return;
                }
                foreach (var path in desktop.Args ?? []) await window.OpenPath(path);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
