using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int DockingChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.DockingSelfCheck(output); Console.WriteLine("PASS: theme rows, compact tabs, close/reopen/drag/dock, window menus, shared document editing/undo/close, guide cursor/selection/menu, pasteboard, text Escape, slider bounds and Camera Raw group reset."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void DockingSelfCheck(string output)
    {
        static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        void Save(Window window, string suffix) { Pump(); window.UpdateLayout(); using var image = window.CaptureRenderedFrame(); image!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        Point At(SKPoint point) => _canvas.TranslatePoint(_canvas.ToScreen(point), this)!.Value;
        var doc = new CanvasDocument(Guid.NewGuid(), 500, 340);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(100, 100)); pixels.Erase(SKColors.CornflowerBlue);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Object"), new(50, 50, 100, 100), "Object"); doc.Layers.Add(layer);
        AdoptImported(doc, "Workspace Check"); SelectLayerRow(layer.ID); Pump();
        var originalTheme = EditorPreferences.Current.Theme;
        foreach (var theme in new[] { 0, 2 })
        {
            EditorPreferences.Current.Theme = theme; ApplyPreferences(); _layers.SelectedItems!.Clear(); Pump();
            var row = _layers.GetVisualDescendants().OfType<ListBoxItem>().First();
            var presenter = row.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
            Check(presenter.Background is ISolidColorBrush fill && fill.Color == ((ISolidColorBrush)Skin.SurfaceDarkBrush).Color, "Layer presenter did not follow theme.");
            ShowDockPanel("Channels"); Pump();
            foreach (var item in _dockPanels["Channels"].Body.GetVisualDescendants().OfType<ListBoxItem>().Where(r => !r.IsSelected))
                Check(item.Background is ISolidColorBrush color && color.Color == ((ISolidColorBrush)Skin.SurfaceDarkBrush).Color, "Channel row did not follow theme.");
            Save(this, "theme-" + theme);
        }
        EditorPreferences.Current.Theme = originalTheme; ApplyPreferences(); ShowDockPanel("Layers");
        foreach (var name in WindowPanelNames)
        {
            OpenWindowPanel(name); Pump(); var panel = _dockPanels[name]; Check(panel.Body is not null && panel.Visible, "Window panel missing: " + name);
            Check(panel.Tab!.FontSize <= 13, "Oversized tab: " + name);
            Click(panel.Tab.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "close-panel-" + name)));
            Check(!panel.Visible && !panel.Tab.IsVisible, "Panel close failed: " + name); OpenWindowPanel(name); HidePanel(panel);
        }
        RawImportSelfCheck();
        DockLayoutSelfCheck(output);
        PanelIconSelfCheck(output);
        ResetDockPanels(); OpenTypePanel("Character"); Pump(); var character = _dockPanels["Character"];
        var handle = character.Tab!.GetVisualDescendants().OfType<TextBlock>().Single(t => Equals(t.Tag, "panel-handle-Character"));
        var handleAt = handle.TranslatePoint(new Point(8, 7), this)!.Value;
        this.MouseDown(handleAt, MouseButton.Left); this.MouseMove(handleAt + new Vector(-300, 90), RawInputModifiers.LeftMouseButton); this.MouseUp(handleAt + new Vector(-300, 90), MouseButton.Left); Pump();
        Check(character.Floating is not null && ReferenceEquals(TopLevel.GetTopLevel(character.Body), character.Floating), "Dragging panel did not create a separate window."); Save(character.Floating!, "floating-character");
        Check(!character.Floating!.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString()?.StartsWith("dock-panel-", StringComparison.Ordinal) == true), "Floating panel still has a fixed-home button.");
        DockPanelBack(character); Pump();
        Check(character.Floating is null && ReferenceEquals(character.Tab!.Content, character.Body), "Docking lost panel content.");
        FloatPanel(character); character.Floating!.Close(); Pump(); Check(!character.Visible, "Native close left floating panel active."); OpenTypePanel("Character");
        Check(character.Visible && character.Tab!.Content == character.Body && character.Floating is not null, "Closed floating panel could not reopen."); DockPanelBack(character);
        EditorPreferences.Current.LockWorkspace = true; FloatPanel(character); Check(character.Floating is null, "Locked workspace detached a panel."); EditorPreferences.Current.LockWorkspace = false;
        ApplyWorkspace("Photography"); Check(_dockPanels["Histogram"].Visible && _dockPanels["Navigator"].Visible, "Photography layout missing panels."); ApplyWorkspace("Essentials");
        EditorPreferences.Current.Workspaces["Dock Test"] = new() { Panels = [new("Character", true, true, Position.X + 90, Position.Y + 120, 350, 520)] };
        EditorPreferences.Current.Save(); Check(EditorPreferences.Load().Workspaces["Dock Test"].Panels[0].Floating, "Workspace persistence lost floating state.");
        ApplyWorkspace("Dock Test"); Check(character.Floating is not null && character.Floating.Width == 350, "Saved workspace did not restore floating geometry.");
        EditorPreferences.Current.Workspaces.Remove("Dock Test"); ApplyWorkspace("Essentials");
        var menu = _mainMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "_Window")); menu.IsSubMenuOpen = true; Pump();
        Check(menu.Items.OfType<MenuItem>().Any(m => Equals(m.Tag, "Arrange")) && menu.Items.OfType<MenuItem>().Any(m => Equals(m.Tag, "Workspace")), "Window menu incomplete."); Check(!menu.Items.OfType<MenuItem>().Any(m => m.Header?.ToString()?.Contains("Exchange", StringComparison.OrdinalIgnoreCase) == true), "Exchange entry remains.");
        Check(!BuildTypeMenu().Items.OfType<MenuItem>().Any(m => m.Header?.ToString()?.Contains("Adobe Fonts", StringComparison.OrdinalIgnoreCase) == true), "Adobe Fonts entry remains."); Save(this, "window-menu"); menu.IsSubMenuOpen = false;

        void Finish(Task task) { for (var i = 0; i < 300 && !task.IsCompleted; i++) { Pump(); Thread.Sleep(10); } Check(task.IsCompleted, "Clipboard did not finish."); task.GetAwaiter().GetResult(); Pump(); }
        void TextKey(Key key) { this.KeyPress(key, RawInputModifiers.Control, PhysicalKey.None, null); this.KeyRelease(key, RawInputModifiers.Control, PhysicalKey.None, null); Pump(); }
        SetTool(Tool.Type); TypeHere(new(220, 220)); _canvas.Focus(); this.KeyTextInput("Clipboard draft"); Pump();
        TextKey(Key.A); Check(_text?.SelectionLength == "Clipboard draft".Length, "Ctrl+A did not select canvas text.");
        TextKey(Key.C); var copiedText = Clipboard!.TryGetTextAsync(); Finish(copiedText); Check(copiedText.Result == "Clipboard draft", "Ctrl+C copied layers instead of selected text.");
        TextKey(Key.X); Check(_text?.Content == "", "Ctrl+X did not cut selected text.");
        Finish(Clipboard!.SetTextAsync("保留文字 ESC")); TextKey(Key.V);
        Check(_text?.Content == "保留文字 ESC", "Ctrl+V did not paste into the active text session.");
        this.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); this.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
        Check(_text is null && doc.Layers.Any(l => l.LiveText?.Content == "保留文字 ESC"), "Escape deleted entered text.");
        TypeHere(new(220, 280)); _canvas.Focus(); this.KeyTextInput("Cancel only this draft");
        this.KeyPress(Key.Escape, RawInputModifiers.Control, PhysicalKey.Escape, null); this.KeyRelease(Key.Escape, RawInputModifiers.Control, PhysicalKey.Escape, null); Pump();
        Check(_text is null && doc.Layers.All(l => l.LiveText?.Content != "Cancel only this draft"), "Ctrl+Escape did not cancel the draft.");

        SetTool(Tool.Move); var guide = new CanvasGuide { ID = Guid.NewGuid(), Axis = GuideAxis.Vertical, Position = 190 }; doc.Guides.Add(guide); _canvas.ShowsGuides = true; Pump();
        var point = At(new(190, 140)); this.MouseMove(point); Pump(); Check(_canvas.ActiveCursorKind == StandardCursorType.SizeWestEast, "Guide hover cursor missing.");
        this.MouseDown(point, MouseButton.Left); this.MouseUp(point, MouseButton.Left); Pump(); Check(_canvas.SelectedGuides.Contains(guide.ID), "Guide selection missing.");
        this.MouseDown(point, MouseButton.Right); this.MouseUp(point, MouseButton.Right); Pump();
        var guideMenu = _canvas.ContextMenu!; Check(guideMenu.Items.OfType<MenuItem>().Count() == 6, "Guide context menu is incomplete."); Save(this, "guide-menu");
        guideMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Clear Selected Guides")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); guideMenu.Close(); Pump();
        Check(doc.Guides.Count == 0, "Clear selected guide failed."); Undo(); Check(doc.Guides.Count == 1, "Guide removal undo failed.");
        Thread.Sleep(650); point = At(new(190,140)); this.MouseDown(point, MouseButton.Left); this.MouseUp(point, MouseButton.Left); this.MouseDown(point, MouseButton.Left); this.MouseUp(point, MouseButton.Left); Pump();
        var guidePreferences = OwnedWindows.OfType<PreferencesDialog>().Single(); Check(guidePreferences.Category == "Guides, Grid and Slices", "Guide double click opened wrong preferences page."); guidePreferences.Close(false); Pump();
        SetPasteboard("Light Gray", "#BFBFBF"); Check(((ISolidColorBrush)Skin.PasteboardBrush).Color.R == 191, "Pasteboard preset not applied."); SetPasteboard("Default", "#282828");

        var shared = _open; var other = DocumentWindow(shared, false); Pump(); other.UpdateLayout();
        SelectLayerRow(layer.ID); Edit("Shared Opacity", () => { doc.Layers[0].Opacity = .42; return true; });
        Check(ReferenceEquals(other._document, doc) && other._document.Layers[0].Opacity == .42, "Separate view did not share changes.");
        other.Undo(); Pump(); Check(doc.Layers[0].Opacity == 1, "Undo from separate view failed.");
        _canvas.RestoreViewport((1.8, 10, 20)); MatchDocumentViews("Match All"); Check(other._canvas.Viewport == _canvas.Viewport, "Match view failed.");
        var sharedStyle = EditLayerStyle(); Pump(); Check(other._sharedViewBlocked, "A shared view could edit during a live layer style preview.");
        OwnedWindows.OfType<LayerStyleDialog>().Single().Close(); Pump(); Check(sharedStyle.IsCompleted && !other._sharedViewBlocked, "Shared view stayed blocked after preview closed.");
        Save(other, "document-window"); var close = other.CloseTab(shared); Pump(); Check(close.IsCompletedSuccessfully && _document!.Layers[0].Asset!.Image.Width == 100, "Closing one view destroyed the other."); other._closeApproved = true; other.Close();

        var raw = new CameraRawPanel(new CameraRawSettings(), SKColors.Red); raw.Move("Exposure, stops", 2); raw.Move("Contrast", 40); raw.Move("Glow range", 80); raw.ResetGroup("Light");
        Check(raw.Current().Exposure == 0 && raw.Current().Contrast == 0 && raw.Current().GlowRange == 80, "Section reset affected a neighboring section.");
        raw.ResetGroup("Effects"); Check(raw.Current().GlowRange == new CameraRawSettings().GlowRange, "Section reset zeroed a nonzero default.");
        raw.Move("Sharpen radius", 20); raw.ResetGroup("Detail"); Check(raw.Current().SharpenRadius == new CameraRawSettings().SharpenRadius, "Detail reset lost default radius.");
        SelectLayerRow(layer.ID); var operation = EditLayerStyle(StyleEffectKind.BevelEmboss); Pump(); var style = OwnedWindows.OfType<LayerStyleDialog>().Single();
        foreach (var slider in style.GetVisualDescendants().OfType<Slider>().ToArray())
        {
            slider.BringIntoView(); style.UpdateLayout();
            double? firstX = null;
            foreach (var value in new[] { slider.Minimum, slider.Maximum })
            {
                slider.Value = value; style.UpdateLayout(); var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single(); var at = thumb.TranslatePoint(default, slider)!.Value;
                if (firstX is { } left) Check(at.X - left > slider.Bounds.Width * .7, "Slider thumb did not follow its value."); else firstX = at.X;
                Check(at.Y >= 0 && at.Y + thumb.Bounds.Height <= slider.Bounds.Height + .1 && at.X >= -.1 && at.X + thumb.Bounds.Width <= slider.Bounds.Width + .1, "Layer style thumb clipped at endpoint.");
            }
        }
        var firstSlider = style.GetVisualDescendants().OfType<Slider>().First(); firstSlider.BringIntoView(); style.UpdateLayout();
        firstSlider.Value = firstSlider.Minimum; style.UpdateLayout(); var dragThumb = firstSlider.GetVisualDescendants().OfType<Thumb>().Single(); var dragAt = dragThumb.TranslatePoint(new Point(7,7), style)!.Value;
        style.MouseDown(dragAt, MouseButton.Left); style.MouseMove(dragAt + new Vector(80,0), RawInputModifiers.LeftMouseButton); style.MouseUp(dragAt + new Vector(80,0), MouseButton.Left); Pump();
        Check(firstSlider.Value > firstSlider.Minimum, "Dragging the style slider did not change its value.");
        Save(style, "style-sliders"); Click(style.GetVisualDescendants().OfType<Button>().First(b => b.IsCancel)); Check(operation.IsCompleted, "Style dialog failed to close.");
        var shortcuts = new ShortcutDialog(new Dictionary<string, ShortcutChord>()); var proofKey = MenuKey("Proof Colors");
        shortcuts.Record(proofKey, Key.Delete, KeyModifiers.None); Check(shortcuts.Draft[proofKey].Key == "Delete", "Delete could not be assigned.");
        shortcuts.Record(proofKey, Key.None, KeyModifiers.None); Check(shortcuts.Draft[proofKey].Key == "Delete", "An unidentified key replaced a shortcut.");
        shortcuts.Record(proofKey, Key.Back, KeyModifiers.None); Check(!shortcuts.Draft[proofKey].IsBound, "Shortcut clear failed.");
        ApplyWorkspace("Essentials"); _canvas.Fit(); Save(this, "final");
    }
}
