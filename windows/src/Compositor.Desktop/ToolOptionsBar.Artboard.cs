using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Compositor.Desktop;

internal sealed class ArtboardToolOptions
{
    public int Width { get; set; } = 750;
    public int Height { get; set; } = 1334;
    public uint Background { get; set; } = 0xffffffff;
}
internal sealed partial class ToolOptionsBar
{
    internal ArtboardToolOptions Artboard { get; } = new();
    internal event Action<string>? ArtboardAsked;
    private readonly ComboBox _artboardPreset = new() { Width = 170 };
    private readonly ComboBox _artboardColor = new() { Width = 100 };
    private readonly Button _artboardSwatch = new() { Width = 22, Height = 22, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(1) };
    private readonly NumericUpDown _artboardWidth = new() { Width = 85, Minimum = 1, Maximum = 30000, ShowButtonSpinner = false, FormatString = "0" };
    private readonly NumericUpDown _artboardHeight = new() { Width = 85, Minimum = 1, Maximum = 30000, ShowButtonSpinner = false, FormatString = "0" };
    internal static readonly (string Name, int Width, int Height)[] ArtboardPresets =
    [
        ("Custom",0,0), ("iPhone X",1125,2436), ("iPhone 8/7/6 Plus",1242,2208), ("iPhone 8/7/6",750,1334),
        ("iPad Pro 12.9",2048,2732), ("iPad Pro 10.5",1668,2224), ("iPad Retina",1536,2048), ("Android 1080p",1080,1920),
        ("Microsoft Surface Pro 4",2736,1824), ("Microsoft Surface Pro 3",2160,1440), ("iPhone 5",640,1136), ("iPad Mini",768,1024),
        ("Apple Watch 42mm",312,390), ("Apple Watch 38mm",272,340), ("Mobile Design",1080,1920),
        ("Web - Most Common",1366,768), ("Web - Minimum",1024,768), ("Web - Small",1280,800), ("Web - Medium",1440,900), ("Web - Large",1920,1080),
        ("MacBook Pro 13 (Retina)",2560,1600), ("MacBook Pro 15 (Retina)",2880,1800), ("iMac 27",2560,1440), ("Desktop HD Design",1920,1080),
        ("iOS 7 iPad App Icon",152,152), ("iOS 7 iPhone App Icon",120,120), ("iOS 7 Spotlight",80,80), ("iOS 7 Settings",58,58),
        ("Mac Icon 1024",1024,1024), ("Mac Icon 512",512,512), ("Mac Icon 256",256,256), ("Mac Icon 128",128,128), ("Mac Icon 48",48,48), ("Mac Icon 32",32,32), ("Mac Icon 16",16,16),
        ("Legacy iPad App Icon",72,72), ("Legacy iPhone App Icon",57,57), ("Legacy iPad Spotlight",50,50)
    ];
    private void BuildArtboardOptions()
    {
        void Label(string text) => Cell("artboardOptions", new TextBlock { Text = Localize.Text(text), VerticalAlignment = VerticalAlignment.Center });
        Label("Size:"); StyleOptionCombo(_artboardPreset); _artboardPreset.ItemsSource = ArtboardPresets.Select(p => Localize.Text(p.Name)).ToArray(); Cell("artboardOptions", _artboardPreset);
        Label("Width:"); _artboardWidth.Height = 24; Cell("artboardOptions", _artboardWidth);
        Label("Height:"); _artboardHeight.Height = 24; Cell("artboardOptions", _artboardHeight);
        _artboardSwatch.Click += (_, _) => ArtboardAsked?.Invoke("Custom Color"); ToolTip.SetTip(_artboardSwatch, Localize.Text("Artboard Background")); Cell("artboardOptions", _artboardSwatch);
        StyleOptionCombo(_artboardColor); _artboardColor.ItemsSource = new[] { "White", "Black", "Transparent", "Other…" }.Select(Localize.Text).ToArray(); Cell("artboardOptions", _artboardColor);
        _artboardPreset.SelectionChanged += (_, _) =>
        {
            if (_loading || _artboardPreset.SelectedIndex <= 0) return;
            var preset = ArtboardPresets[_artboardPreset.SelectedIndex]; Artboard.Width = preset.Width; Artboard.Height = preset.Height;
            RefreshArtboardOptions(); ArtboardAsked?.Invoke("Size");
        };
        void Number() { if (_loading) return; Artboard.Width = (int)(_artboardWidth.Value ?? 1); Artboard.Height = (int)(_artboardHeight.Value ?? 1); ArtboardAsked?.Invoke("Size"); }
        _artboardWidth.ValueChanged += (_, _) => Number(); _artboardHeight.ValueChanged += (_, _) => Number();
        _artboardColor.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (_artboardColor.SelectedIndex == 3) { ArtboardAsked?.Invoke("Custom Color"); return; }
            Artboard.Background = _artboardColor.SelectedIndex switch { 1 => 0xff000000, 2 => 0, _ => 0xffffffff }; ArtboardAsked?.Invoke("Background");
        };
        foreach (var orientation in new[] { "Portrait", "Landscape" })
        {
            var b = IconButton(orientation == "Portrait" ? "portrait" : "landscape", orientation, () =>
            {
                var min = Math.Min(Artboard.Width, Artboard.Height); var max = Math.Max(Artboard.Width, Artboard.Height);
                Artboard.Width = orientation == "Portrait" ? min : max; Artboard.Height = orientation == "Portrait" ? max : min;
                RefreshArtboardOptions(); ArtboardAsked?.Invoke("Size");
            }); Cell("artboardOptions", b);
        }
        Cell("artboardOptions", IconButton("new-artboard", "New Artboard", () => ArtboardAsked?.Invoke("New")));
        var align = IconButton("", "Align Artboards", null); align.Content = new InspectorGlyph("Align Left"); align.Click += (_, _) => MenuAsked?.Invoke("Artboard Alignment", align); Cell("artboardOptions", align);
        var settings = IconButton("settings", "Artboard Settings", null); settings.Click += (_, _) => MenuAsked?.Invoke("Artboard Settings", settings); Cell("artboardOptions", settings);
    }
    internal void RefreshArtboardOptions()
    {
        var loading = _loading; _loading = true;
        _artboardWidth.Value = Artboard.Width; _artboardHeight.Value = Artboard.Height;
        _artboardPreset.SelectedIndex = Math.Max(0, Array.FindIndex(ArtboardPresets, p => p.Width == Artboard.Width && p.Height == Artboard.Height));
        _artboardColor.SelectedIndex = Artboard.Background switch { 0xffffffff => 0, 0xff000000 => 1, 0 => 2, _ => 3 };
        _artboardSwatch.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromUInt32(Artboard.Background));
        _loading = loading;
    }
}
