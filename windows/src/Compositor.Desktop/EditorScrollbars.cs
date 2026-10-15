using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

internal static class EditorScrollbars
{
    internal static void Apply(Styles styles)
    {
        var style = new Style(s => s.OfType<ScrollBar>());
        style.Setters.Add(new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ScrollBar>((bar, names) =>
        {
            var vertical = bar.Orientation == Orientation.Vertical;
            var root = new Grid { Background = Skin.ScrollTrackBrush, RowDefinitions = new(vertical ? "14,*,14" : "*"), ColumnDefinitions = new(vertical ? "*" : "14,*,14") };
            var thumb = new Thumb { Name = "PART_Thumb", Margin = new Thickness(2), Background = Skin.ScrollThumbBrush,
                Template = new FuncControlTemplate<Thumb>((owner, _) => new Border { Background = Skin.ScrollThumbBrush, CornerRadius = new CornerRadius(2) }) };
            thumb.PointerEntered += (_, _) => { if (thumb.GetVisualChildren().FirstOrDefault() is Border b) b.Background = Skin.SurfaceControlHoverBrush; };
            thumb.PointerExited += (_, _) => { if (thumb.GetVisualChildren().FirstOrDefault() is Border b) b.Background = Skin.ScrollThumbBrush; };
            var track = new Track { Name = "PART_Track", Orientation = bar.Orientation, IsDirectionReversed = vertical, Thumb = thumb };
            names.Register("PART_Track", track); names.Register("PART_Thumb", thumb);
            track.Bind(Track.MinimumProperty, new Binding(nameof(bar.Minimum)) { Source = bar });
            track.Bind(Track.MaximumProperty, new Binding(nameof(bar.Maximum)) { Source = bar });
            track.Bind(Track.ValueProperty, new Binding(nameof(bar.Value)) { Source = bar, Mode = BindingMode.TwoWay });
            track.Bind(Track.ViewportSizeProperty, new Binding(nameof(bar.ViewportSize)) { Source = bar });
            RepeatButton Button(bool increase, bool page)
            {
                var button = new RepeatButton { MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    Template = new FuncControlTemplate<RepeatButton>((_, _) => page ? new Border { Background = Brushes.Transparent } : new ScrollArrow(vertical, increase)) };
                button.Click += (_, _) => bar.Value = Math.Clamp(bar.Value + (increase ? 1 : -1) * (page ? bar.LargeChange : bar.SmallChange), bar.Minimum, bar.Maximum); return button;
            }
            track.DecreaseButton = Button(false, true); track.IncreaseButton = Button(true, true);
            var before = Button(false, false); var after = Button(true, false);
            Grid.SetRow(track, vertical ? 1 : 0); Grid.SetColumn(track, vertical ? 0 : 1);
            Grid.SetRow(after, vertical ? 2 : 0); Grid.SetColumn(after, vertical ? 0 : 2);
            root.Children.Add(before); root.Children.Add(track); root.Children.Add(after); return root;
        })));
        styles.Add(style);
        // Fluent chooses orientation templates in trigger styles; match that priority on native windows.
        foreach (var orientation in new[] { ":horizontal", ":vertical" })
        {
            var oriented = new Style(s => s.OfType<ScrollBar>().Class(orientation));
            oriented.Setters.Add(new Setter(TemplatedControl.TemplateProperty, ((Setter)style.Setters[0]).Value)); styles.Add(oriented);
        }
    }
    private sealed class ScrollArrow(bool vertical, bool forward) : Control
    {
        public override void Render(DrawingContext context)
        {
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2); var sign = forward ? 1 : -1; var pen = new Pen(Skin.SecondaryBrush, 1);
            if (vertical) { context.DrawLine(pen, center + new Vector(-3, -sign * 1.5), center + new Vector(0, sign * 1.5)); context.DrawLine(pen, center + new Vector(0, sign * 1.5), center + new Vector(3, -sign * 1.5)); }
            else { context.DrawLine(pen, center + new Vector(-sign * 1.5, -3), center + new Vector(sign * 1.5, 0)); context.DrawLine(pen, center + new Vector(sign * 1.5, 0), center + new Vector(-sign * 1.5, 3)); }
        }
    }
}
