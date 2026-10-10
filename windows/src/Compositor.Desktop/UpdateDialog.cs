using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>A compact Windows update notice with expandable release notes and a link to the release.</summary>
internal sealed class UpdateDialog : DialogWindow
{
    private static readonly IBrush Ink = Skin.LabelBrush;

    private UpdateDialog(string title, string message, string? page, string? notes)
    {
        Title = Localize.Text(title);
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var close = new Button
        {
            Content = Localize.Text("Close"),
            IsCancel = true,
            IsDefault = true,
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(2),
            Background = Skin.AccentBrush,
            BorderBrush = Skin.AccentBrush,
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (page is not null)
        {
            var open = new Button
            {
                Content = Localize.Text("What changed…"),
                MinWidth = 76,
                Height = 26,
                FontSize = 12,
                CornerRadius = new CornerRadius(2),
                Background = Skin.SurfaceControlBrush,
                BorderBrush = Skin.BorderControlBrush,
                BorderThickness = new Thickness(1),
                Foreground = Skin.LabelBrush,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            open.Click += (_, _) => Open(page);
            buttons.Children.Add(open);
        }
        buttons.Children.Add(close);
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = Localize.Text(message), TextWrapping = TextWrapping.Wrap, Foreground = Ink },
                buttons,
            },
        };
        if (!string.IsNullOrWhiteSpace(notes) && Content is StackPanel panel)
        {
            panel.Children.Insert(1, new Expander { Header = Localize.Text("What's New"), IsExpanded = true, Content = new ScrollViewer { MaxHeight = 180, Content = new TextBlock { Text = notes, TextWrapping = TextWrapping.Wrap } } });
        }
        Opened += (_, _) => close.Focus();
    }

    /// <summary>The release page opened in whatever the machine uses for links, and nothing when it cannot be.</summary>
    private static void Open(string page)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page) { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A machine with no browser set up is not worth failing the dialog over: the address is in the box.
        }
    }

    public static async Task Ask(Window owner, string title, string message, string? page = null, string? notes = null)
    {
        await new UpdateDialog(title, message, page, notes).ShowDialog(owner);
    }

    /// <summary>The version this build is: the one in the project file, read back off the assembly.</summary>
    public static AppVersion Running
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? new AppVersion(0, 0, 0, "") : new AppVersion(version.Major, version.Minor, version.Build, "");
        }
    }
}
