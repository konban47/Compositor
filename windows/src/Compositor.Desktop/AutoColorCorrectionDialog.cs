using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>What Auto Color Correction Options set: the algorithm, whether midtones are snapped to neutral, the share of
/// shadows and highlights clipped (percent), and the colors the darkest and lightest pixels are taken to.</summary>
internal sealed record AutoColorOptions(LevelsAuto Mode = LevelsAuto.Brightness, bool SnapNeutral = false, double ShadowClip = .1, double HighlightClip = .1,
    uint Shadows = 0xff000000, uint Midtones = 0xff808080, uint Highlights = 0xffffffff)
{
    internal bool IsValid => Enum.IsDefined(Mode) && ShadowClip is >= 0 and <= 9.99 && HighlightClip is >= 0 and <= 9.99;
}

/// <summary>
/// Photoshop's Auto Color Correction Options, opened by Options… in Levels and Curves: the four algorithms, Snap Neutral
/// Midtones, and the target colors and clipping. Save as defaults keeps them for Auto in later sessions and for Image ▸
/// Auto Tone, Auto Contrast and Auto Color; otherwise they last until the app closes.
/// </summary>
internal sealed class AutoColorCorrectionDialog : DialogWindow
{
    private static AutoColorOptions? _current;
    private static string PathName => System.IO.Path.Combine(AppPaths.SettingsDirectory, "auto-color-correction.json");
    internal static AutoColorOptions Current
    {
        get
        {
            if (_current is not null) return _current;
            try { if (File.Exists(PathName) && JsonSerializer.Deserialize<AutoColorOptions>(File.ReadAllText(PathName)) is { IsValid: true } saved) return _current = saved; }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
            return _current = new AutoColorOptions();
        }
        set => _current = value;
    }

    private AutoColorOptions? _result;

    private AutoColorCorrectionDialog()
    {
        Title = Localize.Text("Auto Color Correction Options");
        SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var start = Current;
        RadioButton Algorithm(string text, char key, LevelsAuto mode)
        {
            var radio = new RadioButton { Content = new TextBlock { Text = Localize.Text(text) + $"({key})" }, GroupName = "algorithm", IsChecked = start.Mode == mode, Tag = mode };
            LayerDialogControls.Key(this, key, radio); return radio;
        }
        var algorithms = new[]
        {
            Algorithm("Enhance Monochromatic Contrast", 'M', LevelsAuto.Contrast), Algorithm("Enhance Per Channel Contrast", 'E', LevelsAuto.Color),
            Algorithm("Find Dark & Light Colors", 'F', LevelsAuto.Neutral), Algorithm("Enhance Brightness and Contrast", 'B', LevelsAuto.Brightness),
        };
        var snap = new CheckBox { Content = new TextBlock { Text = Localize.Text("Snap Neutral Midtones") + "(N)" }, IsChecked = start.SnapNeutral, Tag = "Snap Neutral Midtones" };
        LayerDialogControls.Key(this, 'N', snap);
        var algorithmPanel = new StackPanel { Spacing = 12 }; foreach (var radio in algorithms) algorithmPanel.Children.Add(radio);
        snap.Margin = new Thickness(0, 10, 0, 0); algorithmPanel.Children.Add(snap);

        var colors = new uint[] { start.Shadows, start.Midtones, start.Highlights };
        Button Swatch(int index, string name)
        {
            var swatch = new Button { Width = 60, Height = 32, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Tag = name + " target" };
            void Paint() => swatch.Background = new SolidColorBrush(Color.FromUInt32(colors[index]));
            swatch.Click += async (_, _) =>
            {
                var c = Color.FromUInt32(colors[index]);
                await ColorPickerDialog.Pick(this, name switch { "Shadows" => "Select target shadow color:", "Midtones" => "Select target midtone color:", _ => "Select target highlight color:" }, (c.R / 255.0, c.G / 255.0, c.B / 255.0), moved =>
                {
                    colors[index] = 0xff000000u | (uint)Math.Round(moved.Red * 255) << 16 | (uint)Math.Round(moved.Green * 255) << 8 | (uint)Math.Round(moved.Blue * 255); Paint();
                });
            };
            Paint(); return swatch;
        }
        var shadowClip = new ValueBox(0, 9.99, start.ShadowClip, "0.00", tag: "Shadow Clip"); var highlightClip = new ValueBox(0, 9.99, start.HighlightClip, "0.00", tag: "Highlight Clip");
        LayerDialogControls.Key(this, 'C', shadowClip); LayerDialogControls.Key(this, 'P', highlightClip);
        TextBlock Right(string text) => new() { Text = Localize.Text(text) + ":", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var targets = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,Auto,Auto"), RowDefinitions = new("Auto,Auto,Auto"), ColumnSpacing = 10, RowSpacing = 12 };
        void Put(Control c, int row, int column) { Grid.SetRow(c, row); Grid.SetColumn(c, column); c.VerticalAlignment = VerticalAlignment.Center; targets.Children.Add(c); }
        Put(Right("Shadows"), 0, 0); Put(Swatch(0, "Shadows"), 0, 1); Put(new TextBlock { Text = Localize.Text("Clip") + "(C):", Margin = new Thickness(20, 0, 0, 0) }, 0, 2); Put(shadowClip, 0, 3); Put(new TextBlock { Text = "%" }, 0, 4);
        Put(Right("Midtones"), 1, 0); Put(Swatch(1, "Midtones"), 1, 1);
        Put(Right("Highlights"), 2, 0); Put(Swatch(2, "Highlights"), 2, 1); Put(new TextBlock { Text = Localize.Text("Clip") + "(P):", Margin = new Thickness(20, 0, 0, 0) }, 2, 2); Put(highlightClip, 2, 3); Put(new TextBlock { Text = "%" }, 2, 4);
        var targetGroup = AdjustmentKit.Group("Target Colors & Clipping", targets);
        // Enhance Brightness and Contrast works the picture out as a whole: the snap and the targets do not apply, as in Photoshop.
        void Refresh() { var brightness = algorithms[3].IsChecked == true; snap.IsEnabled = !brightness; targetGroup.IsEnabled = !brightness; targetGroup.Opacity = brightness ? .5 : 1; }
        foreach (var radio in algorithms) radio.IsCheckedChanged += (_, _) => Refresh();
        Refresh();
        var save = new CheckBox { Content = new TextBlock { Text = Localize.Text("Save as defaults") + "(D)" }, Tag = "Save as defaults" };
        LayerDialogControls.Key(this, 'D', save);
        var left = new StackPanel { Width = 400, Children = { AdjustmentKit.Group("Algorithms", algorithmPanel), targetGroup, new Border { Height = 14 }, save } };
        var actions = AdjustmentKit.Actions(this, () =>
        {
            var mode = (LevelsAuto)algorithms.First(r => r.IsChecked == true).Tag!;
            _result = new AutoColorOptions(mode, snap.IsChecked == true, shadowClip.Value, highlightClip.Value, colors[0], colors[1], colors[2]);
            Current = _result;
            if (save.IsChecked == true)
                try { Directory.CreateDirectory(AppPaths.SettingsDirectory); File.WriteAllText(PathName, JsonSerializer.Serialize(_result)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Close();
        }, Close, null);
        actions.Width = 90;
        Content = AdjustmentKit.Body(left, actions);
    }

    internal static async Task<AutoColorOptions?> Ask(Window owner)
    {
        var dialog = new AutoColorCorrectionDialog();
        await dialog.ShowDialog(owner);
        return dialog._result;
    }
}
