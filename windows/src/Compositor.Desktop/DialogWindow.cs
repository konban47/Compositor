using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace Compositor.Desktop;

/// <summary>
/// What every dialog of this app is: a window on the editor's own chrome. Fluent draws a window's background
/// itself — white in its light palette, black in its dark one — and the Mac has one appearance and paints its
/// dialogs the same 0.14 grey as the editor behind them (ContentView's <c>Color(white: 0.14)</c>), so they all
/// start from here rather than each remembering to say so.
/// </summary>
internal abstract class DialogWindow : Window
{
    protected DialogWindow()
    {
        Localize.Prepare(this);
        Background = Skin.ChromeBrush;
        // A plain TextBlock has no foreground of its own, so on a dark dialog it would draw black on near
        // black. Setting the inherited one leaves every control that says what its own text is — a button, a
        // field, a menu row — exactly as the theme drew it.
        TextElement.SetForeground(this, Skin.LabelBrush);
        Opened += (_, _) => Dispatcher.UIThread.Post(FitTextButtons, DispatcherPriority.Loaded);
    }

    // Explicit 24px heights and fixed English widths must not clip localized labels or high-DPI fonts.
    internal void FitTextButtons()
    {
        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            var text = button.Content as string ?? (button.Content as TextBlock)?.Text;
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2) continue;
            TextBlock.SetTextWrapping(button, TextWrapping.Wrap);
            foreach (var presenter in button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
                presenter.TextWrapping = TextWrapping.Wrap;
            if (button.Content is TextBlock label) { label.TextWrapping = TextWrapping.Wrap; label.TextAlignment = TextAlignment.Center; }
            button.Height = double.NaN;
            button.MinHeight = Math.Max(32, button.MinHeight);
            button.Padding = new Thickness(10, 5);
            button.VerticalContentAlignment = VerticalAlignment.Center;
            var available = double.IsFinite(button.Width) ? button.Width : button.Bounds.Width > 0 ? button.Bounds.Width : 260;
            button.Measure(new Size(Math.Max(24, available), double.PositiveInfinity));
            if (button.Parent is Grid grid && grid.RowDefinitions.Count > Grid.GetRow(button))
            {
                var row = grid.RowDefinitions[Grid.GetRow(button)];
                if (row.Height.IsAbsolute && row.Height.Value < button.DesiredSize.Height) row.Height = GridLength.Auto;
            }
            if (button.Parent is Layoutable parent && double.IsFinite(parent.Height) && parent.Height < button.DesiredSize.Height)
                parent.Height = double.NaN;
        }
    }
}
