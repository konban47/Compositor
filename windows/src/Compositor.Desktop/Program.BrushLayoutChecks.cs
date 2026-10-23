using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void BrushLayoutSelfCheck(string output)
    {
        static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        Dispatcher.UIThread.RunJobs(); UpdateLayout();
        var entry = _dockPanels["Brush Settings"]; FloatPanel(entry); var window = entry.Floating!;
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Pump(); var editor = _brushSettingsEditor!; var oldTheme = EditorPreferences.Current.Theme;
        T Find<T>(string tag) where T : Control => editor.GetVisualDescendants().OfType<T>().Single(c => Equals(c.Tag, tag));
        void Save(string suffix) { Pump(); using var shot = window.CaptureRenderedFrame(); shot!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        SetTool(Tool.Brush); ChangePainting(p => p with { TransferEnabled = true }); editor.SelectSection(6); Pump();
        Check(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString()?.StartsWith("caption-") == true), "Floating panel acquired document caption buttons.");
        Check(editor.GetVisualDescendants().OfType<Button>().Count(b => b.Tag?.ToString()?.StartsWith("brush-section-") == true) == 13, "Brush categories are not persistent.");
        Check(Find<StackPanel>("brush-transfer-WetJitter").IsEnabled == false && Find<StackPanel>("brush-transfer-MixJitter").IsEnabled == false, "Mixer-only fields should be disabled.");
        Find<NumericUpDown>("brush-field-OpacityJitter-value").Value = 37; Check(Math.Abs(PaintingOptions.OpacityJitter - .37) < .001, "Numeric jitter does not change the engine.");
        Find<Slider>("brush-field-OpacityJitter-slider").Value = 52; Check(Math.Abs(PaintingOptions.OpacityJitter - .52) < .001 && Find<NumericUpDown>("brush-field-OpacityJitter-value").Value == 52, "Slider and numeric jitter disagree.");
        var control = Find<ComboBox>("brush-field-OpacityControl-choice"); control.SelectedIndex = (int)BrushControl.PenPressure;
        Check(Find<Grid>("brush-field-MinimumOpacity").IsEnabled, "Minimum remains disabled for pressure.");
        control.SelectedIndex = (int)BrushControl.Fade; Find<NumericUpDown>("brush-fade-OpacityControl").Value = 150;
        Check(PaintingOptions.FadeSteps == 150, "Fade steps were not applied.");
        control.SelectedIndex = (int)BrushControl.Off;
        Check(!Find<Grid>("brush-field-MinimumOpacity").IsEnabled && !Find<NumericUpDown>("brush-fade-OpacityControl").IsEnabled, "Off leaves control-dependent parameters enabled.");
        var enabled = Find<CheckBox>("brush-enable-6"); enabled.IsChecked = false;
        Check(!Find<NumericUpDown>("brush-field-OpacityJitter-value").IsEffectivelyEnabled, "Category switch does not disable its parameters.");
        Find<Button>("brush-section-6").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(PaintingOptions.TransferEnabled, "Selecting a category did not enable it.");
        SetTool(Tool.MixerBrush); Pump();
        Check(Find<StackPanel>("brush-transfer-WetJitter").IsEnabled && Find<StackPanel>("brush-transfer-MixJitter").IsEnabled, "Mixer controls did not refresh after changing tools.");
        SetTool(Tool.Brush); ChangePainting(p => p with { OpacityJitter = 0, FlowJitter = 0 }); Pump();
        Check(!Find<StackPanel>("brush-transfer-WetJitter").IsEnabled && Find<NumericUpDown>("brush-field-OpacityJitter-value").Value == 0, "Options toolbar changes did not refresh the settings panel.");
        editor.SelectSection(0); _options.Brush = _options.Brush with { Diameter = 67 }; OptionsChanged(); Pump();
        Check(Find<NumericUpDown>("brush-field-Size-value").Value == 67, "Brush size is stale after changing toolbar options."); editor.SelectSection(6);
        foreach (var theme in new[] { 0, 1, 2, 3 })
        {
            EditorPreferences.Current.Theme = theme; ((DesktopApp)Application.Current!).RefreshAppearance(); editor.Refresh(); Pump();
            Save("BrushTransfer-theme-" + theme);
        }
        window.Width = 340; window.Height = 480; Pump(); Save("BrushTransfer-narrow");
        var parameterScroll = Find<ScrollViewer>("brush-parameter-scroll");
        void CheckWidths()
        {
            foreach (var item in parameterScroll.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is NumericUpDown or ComboBox or Slider))
            {
                var point = item.TranslatePoint(default, parameterScroll)!.Value;
                Check(point.X >= 0 && point.X + item.Bounds.Width <= parameterScroll.Viewport.Width + 1, "A brush parameter is obscured by its scrollbar.");
            }
        }
        CheckWidths(); Find<ComboBox>("brush-field-OpacityControl-choice").SelectedIndex = (int)BrushControl.Fade; Pump(); CheckWidths();
        Find<ComboBox>("brush-field-OpacityControl-choice").SelectedIndex = (int)BrushControl.Off; Pump();
        var preview = Find<Border>("brush-stroke-preview"); var footer = Find<Border>("brush-settings-footer");
        var before = preview.TranslatePoint(default, window)!.Value;
        var scroll = Find<ScrollViewer>("brush-parameter-scroll"); scroll.Offset = new Vector(0, 2000); Pump();
        Check(preview.TranslatePoint(default, window)!.Value == before, "Scrolling parameters moved the stroke preview.");
        Check(footer.TranslatePoint(default, window)!.Value.Y + footer.Bounds.Height <= window.ClientSize.Height + 1, "Preset footer is outside the panel.");
        window.Width = 500; window.Height = 810; scroll.Offset = default; Pump();
        CheckWidths();
        foreach (var item in editor.GetVisualDescendants().OfType<NumericUpDown>().Where(c => c.IsVisible))
        {
            var point = item.TranslatePoint(default, editor)!.Value;
            Check(point.X >= 0 && point.X + item.Bounds.Width <= editor.Bounds.Width + 1, "A brush value is clipped horizontally.");
        }
        EditorPreferences.Current.Theme = oldTheme; ((DesktopApp)Application.Current!).RefreshAppearance();
        DockPanelBack(entry); SetTool(Tool.Brush); Pump();
    }
}

