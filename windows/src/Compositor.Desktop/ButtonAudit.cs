using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

/// <summary>
/// With COMPOSITOR_BUTTON_AUDIT set to a file path, every button that appears while the app runs is written there when
/// nothing would happen on clicking it. The self-checks walk the app, so running them under this finds dead buttons in
/// every panel and dialog they reach.
/// </summary>
internal static class ButtonAudit
{
    private static readonly FieldInfo? Handlers = typeof(Interactive).GetField("_eventHandlers", BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool Listens(Interactive control, RoutedEvent routed) => Handlers?.GetValue(control) is IDictionary table && table[routed] is ICollection { Count: > 0 };

    /// <summary>Whether clicking the button does anything: its own handlers, a command, a flyout, or a handler on the way up.</summary>
    internal static bool IsLive(Button button)
    {
        if (button.Command is not null || button.Flyout is not null || button.ContextMenu is not null) return true;
        if (Listens(button, Button.ClickEvent) || Listens(button, InputElement.PointerPressedEvent) || Listens(button, InputElement.PointerReleasedEvent) || Listens(button, InputElement.TappedEvent)) return true;
        return button.GetVisualAncestors().OfType<Interactive>().Any(parent => Listens(parent, Button.ClickEvent));
    }

    internal static bool Install()
    {
        if (Environment.GetEnvironmentVariable("COMPOSITOR_BUTTON_AUDIT") is not { Length: > 0 } path) return false;
        Control.LoadedEvent.AddClassHandler<Button>((button, _) => Dispatcher.UIThread.Post(() =>
        {
            // Template parts (scroll bars, spinners, combo boxes) and toggles that are read when a dialog closes aren't what this looks for.
            if (button.TemplatedParent is not null || button is ToggleButton || IsLive(button)) return;
            var owner = TopLevel.GetTopLevel(button)?.GetType().Name ?? "detached";
            var label = button.Content is string text ? text : button.Content?.GetType().Name;
            var parents = string.Join(" < ", button.GetVisualAncestors().OfType<Control>().Select(c => c.Tag as string).Where(t => t is { Length: > 0 }).Take(3));
            File.AppendAllText(path, $"{owner}\ttag={button.Tag}\ttip={ToolTip.GetTip(button)}\tlabel={label}\tenabled={button.IsEnabled}\tin={parents}{Environment.NewLine}");
        }, DispatcherPriority.Background));
        return true;
    }
}