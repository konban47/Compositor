using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int PanelWorkflowChecks(string output)
    {
        EditorPreferences.Current.Theme = 1;
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.PanelWorkflowSelfCheck(output); Console.WriteLine("PASS: shared themed captions, shortcut grouping without scrolling, complete customization rows, layer filters/opacity, slice options button/commit/cancel/undo and generated icons."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void PanelWorkflowSelfCheck(string output)
    {
        static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); foreach (var w in OwnedWindows) w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Click(Window owner, Control control)
        {
            owner.UpdateLayout(); var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), owner)!.Value;
            owner.MouseDown(p, MouseButton.Left); owner.MouseUp(p, MouseButton.Left); Pump();
        }
        void Save(Window owner, string suffix) { Pump(); using var frame = owner.CaptureRenderedFrame(); frame!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        var doc = LayerPlacement.NewDocument(500, 350)!; AdoptImported(doc, "Panel workflow");
        var shape = ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Rectangle }, SKRectI.Create(40, 40, 120, 90), null)!.Value;
        ShowLayers(doc); SelectLayerRow(shape); Pump();
        var separated = ToolCatalog.Defaults(); separated.Groups = separated.Groups.SelectMany(g => g).Select(id => new List<string> { id }).ToList();
        _rail.ApplyLayout(separated); Pump();
        Check(new[] { Tool.PerspectiveCrop, Tool.Slice, Tool.SliceSelect }.All(t => ReferenceEquals(_rail.ButtonFor(t), _rail.ButtonFor(Tool.Crop))), "C tools were not grouped after loading a separated layout.");
        Check(!_rail.GetVisualDescendants().OfType<ScrollViewer>().Any(), "The rail still scrolls.");
        var last = _rail.GetVisualDescendants().OfType<Button>().Last();
        Check(last.TranslatePoint(new Point(0, last.Bounds.Height), _rail)!.Value.Y <= _rail.Bounds.Height + 1, "Toolbar footer is clipped.");
        Height = 1400; Pump();
        var moveY = _rail.ButtonFor(Tool.Move)!.TranslatePoint(default, _rail)!.Value.Y;
        var marqueeY = _rail.ButtonFor(Tool.Marquee)!.TranslatePoint(default, _rail)!.Value.Y;
        Check(Math.Abs(marqueeY - moveY) <= 37, "A tall window spreads tool buttons apart.");
        Height = 820; Pump();
        var custom = new ToolbarDialog(separated) { Width = 800, Height = 620 }; custom.Show(this); Pump();
        foreach (var list in custom.GetVisualDescendants().OfType<ListBox>())
        {
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Check(!scroll.AllowAutoHide && scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "Customization scrollbars overlay content.");
            foreach (var text in list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.TextWrapping == TextWrapping.Wrap)) Check(text.Bounds.Width > 70, "Tool name column is too narrow.");
        }
        Save(custom, "toolbar"); custom.Close(); Pump();
        _layerKind.SelectedIndex = 4; Pump(); Check(_rows.SequenceEqual([shape]), "Shape layer filter failed.");
        Click(this, _layerFilterButtons[1]); Check(_rows.Count == 2, "Multiple kind filters did not combine.");
        var enabled = this.GetVisualDescendants().OfType<ToggleButton>().Single(b => Equals(b.Tag, "layer-filter-enabled")); Click(this, enabled); Check(_rows.Count == 2, "Disabling layer filters hid layers.");
        Click(this, enabled); _layerKind.SelectedIndex = 6; Pump(); Check(_rows.Count == 0, "Locked filter included unlocked layers.");
        _layerKind.SelectedIndex = 0; SelectLayerRow(shape); Pump(); _opacityValue.Value = 58; Pump();
        Check(Math.Abs(doc.Layers.Single(l => l.ID == shape).Opacity - .58) < .001, "Layer opacity field did not apply."); Undo(); Pump();
        SetTool(Tool.SliceSelect); _selectedMark = null; Pump();
        var sliceButton = _optionsBar.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "Slice Options"));
        Click(this, sliceButton); var form = OwnedWindows.OfType<FormDialog>().Single();
        var properties = form.GetVisualDescendants().OfType<Grid>().Single(g => Equals(g.Tag, "slice-properties-layout"));
        Check(properties.ColumnDefinitions.Count == 2, "Slice dialog actions are not at the right.");
        var xField = form.GetVisualDescendants().OfType<NumericUpDown>().Single(n => Equals(n.Tag, "X:"));
        var wField = form.GetVisualDescendants().OfType<NumericUpDown>().Single(n => Equals(n.Tag, "W:"));
        Check(Math.Abs(xField.TranslatePoint(default, form)!.Value.Y - wField.TranslatePoint(default, form)!.Value.Y) < 1, "Slice dimensions are not in paired columns.");
        var name = form.GetVisualDescendants().OfType<TextBox>().Single(t => Equals(t.Tag, "Name:")); name.Text = "Review slice";
        for (var theme = 0; theme < 4; theme++)
        {
            EditorPreferences.Current.Theme = theme; ((DesktopApp)Application.Current!).RefreshAppearance(); Pump();
            var caption = form.GetVisualDescendants().OfType<DockPanel>().Single(p => Equals(p.Tag, "dialog-title-chrome"));
            var mainCaption = this.GetVisualDescendants().OfType<DockPanel>().Single(p => Equals(p.Tag, "main-menu-chrome"));
            Check(caption.Bounds.Height == mainCaption.Bounds.Height && caption.Background is ISolidColorBrush brush && brush.Color == Skin.Chrome, "Dialog and editor title bars differ.");
            Save(form, "slice-theme-" + theme);
            foreach (var owner in new Window[] { this, form })
            {
                var close = owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "caption-Close"));
                owner.MouseMove(close.TranslatePoint(new Point(12, 12), owner)!.Value); Pump();
                var face = close.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single();
                Check(face.Background is ISolidColorBrush hover && hover.Color == Color.Parse("#C42B1C"), "Close hover is not red.");
                if (theme == 1) Save(owner, owner == this ? "editor-close-hover" : "dialog-close-hover");
                owner.MouseMove(new Point(100, 100)); Pump();
                foreach (var symbol in new[] { "Minimize", "Maximize / Restore" })
                {
                    var button = owner.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => Equals(b.Tag, "caption-" + symbol));
                    if (button is null) continue;
                    Check(button.Bounds.Width == 46 && button.Bounds.Height == 32 && button.CornerRadius == new CornerRadius(0), "Caption hit region is not rectangular.");
                    var point = button.TranslatePoint(new Point(12, 12), owner)!.Value; owner.MouseMove(point); Pump();
                    var surface = button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single();
                    Check(surface.Background is ISolidColorBrush hoverFill && hoverFill.Color == Skin.CaptionHover, "Caption hover did not follow the theme.");
                    Check(theme < 2 ? Skin.CaptionHover.R > Skin.Chrome.R : Skin.CaptionHover.R < Skin.Chrome.R, "Caption hover contrast has the wrong direction.");
                    Save(owner, "caption-theme-" + theme + "-" + (symbol == "Minimize" ? "minimize" : "maximize"));
                    owner.MouseDown(point, MouseButton.Left); Pump();
                    Check(surface.Background is ISolidColorBrush pressed && pressed.Color == Skin.CaptionPressed, "Caption pressed state is missing.");
                    owner.MouseMove(new Point(100, 100)); owner.MouseUp(new Point(100, 100), MouseButton.Left); Pump();
                }
            }
        }
        var maximize = this.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "caption-Maximize / Restore"));
        Click(this, maximize); Check(WindowState == WindowState.Maximized, "Maximize button failed.");
        Click(this, maximize); Check(WindowState == WindowState.Normal, "Restore button failed.");
        Click(this, this.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "caption-Minimize"))); Check(WindowState == WindowState.Minimized, "Minimize button failed."); WindowState = WindowState.Normal; Pump();
        EditorPreferences.Current.Theme = 1; ((DesktopApp)Application.Current!).RefreshAppearance();
        Click(form, form.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Localize.Text("OK"))));
        Check(doc.Marks.Single(m => m.Kind == MarkKind.Slice).Name == "Review slice", "The real Slice Options button did not commit properties.");
        Click(this, sliceButton); form = OwnedWindows.OfType<FormDialog>().Single();
        form.GetVisualDescendants().OfType<TextBox>().Single(t => Equals(t.Tag, "Name:")).Text = "Cancelled";
        Click(form, form.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Localize.Text("Cancel"))));
        Check(doc.Marks.Single().Name == "Review slice", "Cancel changed the slice."); Undo(); Pump(); Check(doc.Marks.Count == 0, "Slice properties undo did not restore automatic slice.");
        Save(this, "editor");
        var icons = _optionsBar.GetVisualDescendants().OfType<SliceOrderIcon>().ToArray(); Check(icons.Length == 4, "The four generated ordering icons are missing.");
        var preview = new Window { Width = 420, Height = 110, Background = Skin.ChromeBrush };
        var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 14, Margin = new Thickness(18) };
        foreach (var icon in new[] { "front", "forward", "backward", "back" }) strip.Children.Add(new SliceOrderIcon(icon) { Width = 72, Height = 72 });
        preview.Content = strip; preview.Show(this); Save(preview, "icons"); preview.Close();
        OpenToolPresets(_optionsBar, manager: true); Pump();
        var manager = OwnedWindows.OfType<ToolPresetManagerWindow>().Single();
        Check(manager.GetVisualDescendants().OfType<DockPanel>().Any(p => Equals(p.Tag, "dialog-title-chrome")), "Tool preset manager did not use themed caption.");
        Save(manager, "preset-manager"); manager.Close();
    }
}
