using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;

namespace Compositor.Desktop;

/// <summary>
/// The panel for the filters that are not Camera Raw — vignette, tonal contrast or lens correction. Each is
/// one short group of amounts, and Apply runs it over the selected layer's pixels. Avalonia ships no such
/// dialog, so this is one.
/// </summary>
internal sealed class FilterDialog : DialogWindow
{
    /// <summary>
    /// Asks for the picture to be shown with this filter's amounts as they stand, or — with nothing — shown as
    /// it is, which is what the Preview tick being off means. The Mac's own filter sheets have that tick.
    /// </summary>
    public Action<FilterSettings?>? Preview { get; set; }

    private readonly CheckBox _preview = new() { Content = Localize.Text("Preview"), IsChecked = true };

    private readonly List<(Slider Slider, Action<FilterSettings, double> Set)> _rows = [];
    private readonly List<double> _fallbacks = [];
    private readonly List<(ComboBox Box, int Fallback, Action<FilterSettings,int> Set)> _choices = [];
    private readonly List<(CheckBox Box, Action<FilterSettings, bool> Set, bool Fallback)> _checks = [];
    /// <summary>The colours the panel offers as swatches, and what they started as for Reset.</summary>
    private readonly List<(ColorSwatch Swatch, Action<FilterSettings, (double Red, double Green, double Blue)> Set,
        (double Red, double Green, double Blue) Fallback)> _swatches = [];
    /// <summary>
    /// The amounts being edited: a copy of the ones the panel was opened with, which are the amounts that
    /// filter was last used with. Editing a copy is what lets a Cancel leave them as they were.
    /// </summary>
    private readonly FilterSettings _amounts;
    private FilterSettings? _result;

    private FilterDialog(FilterKind kind, FilterSettings start)
    {
        _amounts = start.Copy();
        Title = Localize.Text(kind switch
        {
            FilterKind.RgbSplit => "RGB Split",
            FilterKind.LensEffects => "Lens Effects",
            FilterKind.ProgressiveBlur => "Progressive Blur",
            FilterKind.Scanlines => "Scanlines",
            FilterKind.GaussianBlur => "Gaussian Blur",
            FilterKind.MotionBlur => "Motion Blur",
            FilterKind.BloomGlow => "Bloom / Glow",
            FilterKind.AddNoise => "Add Noise",
            FilterKind.Vignette => "Vignette",
            FilterKind.Sharpen => "Sharpen",
            FilterKind.TonalContrast => "Tonal Contrast",
            FilterKind.RimLight => "Rim Light",
            FilterKind.VolumetricLight => "Volumetric Light",
            _ => "Lens Correction",
        });
        Width = 420;
        Height = 360;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var defaults = new FilterSettings();
        var group = new StackPanel { Margin = new Thickness(16), Spacing = 4 };
        switch (kind)
        {
            case FilterKind.LensEffects:
                Height = 620;
                var lensGroups = new Dictionary<LensEffect, StackPanel>();
                Choice(group, "Lens Effect", ["Anamorphic Flare", "Starburst", "Mist", "Prism", "Bokeh"], (int)start.Lens.Effect, 0, (s, v) => {
                    s.Lens.Effect = (LensEffect)v;
                    foreach (var pair in lensGroups) pair.Value.IsVisible = (int)pair.Key == v;
                });
                foreach (var effect in Enum.GetValues<LensEffect>())
                {
                    var lensGroup = new StackPanel { Spacing = 4, IsVisible = effect == start.Lens.Effect };
                    lensGroups.Add(effect, lensGroup); group.Children.Add(lensGroup);
                switch (effect)
                {
                    case LensEffect.AnamorphicFlare:
                        Add(lensGroup, "Threshold", 50, 100, start.Lens.Threshold, 90, (s,v) => s.Lens.Threshold=v);
                        Add(lensGroup, "Amount", 0, 100, start.Lens.FlareAmount, 50, (s,v) => s.Lens.FlareAmount=v);
                        Add(lensGroup, "Length (%)", 1, 100, start.Lens.FlareLength, 60, (s,v) => s.Lens.FlareLength=v);
                        Add(lensGroup, "Blue", 0, 100, start.Lens.FlareBlue, 80, (s,v) => s.Lens.FlareBlue=v); break;
                    case LensEffect.Starburst:
                        Add(lensGroup, "Threshold", 50, 100, start.Lens.Threshold, 90, (s,v) => s.Lens.Threshold=v);
                        Add(lensGroup, "Amount", 0, 100, start.Lens.StarAmount, 50, (s,v) => s.Lens.StarAmount=v);
                        Add(lensGroup, "Length (%)", 2, 40, start.Lens.StarLength, 12, (s,v) => s.Lens.StarLength=v);
                        Choice(lensGroup, "Points", ["4", "6", "8", "12", "14"], Array.IndexOf(new[]{4,6,8,12,14}, start.Lens.StarPoints), 1, (s,v) => s.Lens.StarPoints=new[]{4,6,8,12,14}[v]);
                        Add(lensGroup, "Angle, degrees", -180, 180, start.Lens.StarAngle, 15, (s,v) => s.Lens.StarAngle=v); break;
                    case LensEffect.Mist:
                        Add(lensGroup, "Strength", 0, 100, start.Lens.MistStrength, 50, (s,v) => s.Lens.MistStrength=v);
                        Add(lensGroup, "Size", 0, 100, start.Lens.MistSize, 30, (s,v) => s.Lens.MistSize=v); break;
                    case LensEffect.Prism:
                        Add(lensGroup, "Amount", 0, 100, start.Lens.PrismAmount, 80, (s,v) => s.Lens.PrismAmount=v);
                        Add(lensGroup, "Copies", 1, 8, start.Lens.PrismCopies, 5, (s,v) => s.Lens.PrismCopies=(int)Math.Round(v), "0");
                        Add(lensGroup, "Offset (%)", 0, 50, start.Lens.PrismOffset, 6, (s,v) => s.Lens.PrismOffset=v);
                        Add(lensGroup, "Angle, degrees", -180, 180, start.Lens.PrismAngle, 180, (s,v) => s.Lens.PrismAngle=v);
                        Check(lensGroup, "Both Ways", start.Lens.PrismBothWays, (s,v) => s.Lens.PrismBothWays=v);
                        Add(lensGroup, "Blur", 0, 100, start.Lens.PrismBlur, 0, (s,v) => s.Lens.PrismBlur=v);
                        Add(lensGroup, "Fringe", 0, 100, start.Lens.PrismFringe, 30, (s,v) => s.Lens.PrismFringe=v); break;
                    case LensEffect.Bokeh:
                        Add(lensGroup, "Size (%)", .5, 6, start.Lens.BokehRadius, 2, (s,v) => s.Lens.BokehRadius=v);
                        Add(lensGroup, "Brightness", 0, 100, start.Lens.BokehBrightness, 50, (s,v) => s.Lens.BokehBrightness=v);
                        Add(lensGroup, "Ring", 0, 100, start.Lens.BokehRing, 100, (s,v) => s.Lens.BokehRing=v);
                        Choice(lensGroup, "Aperture", ["Round", "Hexagon", "Oval", "Cat's Eye"], (int)start.Lens.BokehShape, 0, (s,v) => s.Lens.BokehShape=(BokehShape)v); break;
                }
                }
                break;
            case FilterKind.RgbSplit:
                Height = 650;
                Add(group, "Distance, pixels", 0, 200, start.RgbSplitDistance, 10, (s, v) => s.RgbSplitDistance = v);
                Add(group, "Blur", 0, 400, start.RgbSplitBlur, 0, (s, v) => s.RgbSplitBlur = v);
                Add(group, "Angle, degrees", -180, 180, start.RgbSplitAngle, 0, (s, v) => s.RgbSplitAngle = v);
                Check(group, "Radial", start.RgbSplitRadial, (s, v) => s.RgbSplitRadial = v);
                Add(group, "Center X (%)", -100, 200, start.RgbSplitCenterX, 50, (s, v) => s.RgbSplitCenterX = v);
                Add(group, "Center Y (%)", -100, 200, start.RgbSplitCenterY, 50, (s, v) => s.RgbSplitCenterY = v);
                Add(group, "Reach (px)", 1, 100000, start.RgbSplitReach, 1000, (s, v) => s.RgbSplitReach = v);
                break;
            case FilterKind.ProgressiveBlur:
                Height = 620;
                Add(group, "From Radius (px)", 0, 250, start.ProgressiveFrom, 0, (s, v) => s.ProgressiveFrom = v);
                Add(group, "To Radius (px)", 0, 250, start.ProgressiveTo, 20, (s, v) => s.ProgressiveTo = v);
                Add(group, "Start X (%)", -100, 200, start.ProgressiveStartX, 50, (s, v) => s.ProgressiveStartX = v);
                Add(group, "Start Y (%)", -100, 200, start.ProgressiveStartY, 35, (s, v) => s.ProgressiveStartY = v);
                Add(group, "End X (%)", -100, 200, start.ProgressiveEndX, 50, (s, v) => s.ProgressiveEndX = v);
                Add(group, "End Y (%)", -100, 200, start.ProgressiveEndY, 100, (s, v) => s.ProgressiveEndY = v);
                Check(group, "Radial", start.ProgressiveRadial, (s, v) => s.ProgressiveRadial = v);
                Check(group, "Invert", start.ProgressiveInverted, (s, v) => s.ProgressiveInverted = v);
                break;
            case FilterKind.Scanlines:
                Height = 690;
                Add(group, "Line Spacing", 2, 32, start.Scanlines.Spacing, 4, (s, v) => s.Scanlines.Spacing = Math.Round(v), "0");
                Add(group, "Thickness", 5, 100, start.Scanlines.Thickness, 70, (s, v) => s.Scanlines.Thickness = v);
                Add(group, "Dots by Tone", 0, 100, start.Scanlines.Dots, 0, (s, v) => s.Scanlines.Dots = v);
                Add(group, "Wobble", 0, 64, start.Scanlines.Wobble, 0, (s, v) => s.Scanlines.Wobble = v);
                Add(group, "Displace", -100, 100, start.Scanlines.Displace, 0, (s, v) => s.Scanlines.Displace = v);
                Add(group, "Smoothness", 0, 100, start.Scanlines.Smoothness, 50, (s, v) => s.Scanlines.Smoothness = v);
                Add(group, "Threshold", 0, 100, start.Scanlines.Threshold, 0, (s, v) => s.Scanlines.Threshold = v);
                Add(group, "Color Split", 0, 16, start.Scanlines.Split, 0, (s, v) => s.Scanlines.Split = v);
                Add(group, "Density", -100, 100, start.Scanlines.Density, 0, (s, v) => s.Scanlines.Density = v);
                Add(group, "Contrast", -100, 100, start.Scanlines.Contrast, 0, (s, v) => s.Scanlines.Contrast = v);
                Add(group, "Black Level", 0, 100, start.Scanlines.BlackLevel, 0, (s, v) => s.Scanlines.BlackLevel = v);
                Add(group, "Glow", 0, 100, start.Scanlines.Glow, 0, (s, v) => s.Scanlines.Glow = v);
                Check(group, "Original Colors", start.Scanlines.OriginalColors, (s, v) => s.Scanlines.OriginalColors = v);
                Swatch(group, "Dark Color", (start.Scanlines.DarkRed, start.Scanlines.DarkGreen, start.Scanlines.DarkBlue), (0, 0, 0),
                    (s, c) => { s.Scanlines.DarkRed = c.Red; s.Scanlines.DarkGreen = c.Green; s.Scanlines.DarkBlue = c.Blue; }, "Dark Color", "Choose the screen color");
                Swatch(group, "Light Color", (start.Scanlines.LightRed, start.Scanlines.LightGreen, start.Scanlines.LightBlue), (1, 1, 1),
                    (s, c) => { s.Scanlines.LightRed = c.Red; s.Scanlines.LightGreen = c.Green; s.Scanlines.LightBlue = c.Blue; }, "Light Color", "Choose the line color");
                break;
            case FilterKind.Sharpen:
                Add(group, "Amount, %", 0, 500, start.SharpenAmount, defaults.SharpenAmount, (s, v) => s.SharpenAmount = v);
                Add(group, "Radius, pixels", .1, 100, start.SharpenRadius, defaults.SharpenRadius, (s, v) => s.SharpenRadius = v);
                break;
            case FilterKind.GaussianBlur:
                Add(group, "Radius, pixels", 0.1, 250, start.BlurRadius, defaults.BlurRadius, (s, v) => s.BlurRadius = v);
                Check(group, "Preserve Transparency", start.PreserveTransparency, (s, v) => s.PreserveTransparency = v);
                break;
            case FilterKind.BloomGlow:
                Add(group, "Amount", 0, 100, start.BloomAmount, defaults.BloomAmount, (s, v) => s.BloomAmount = v);
                Add(group, "Radius, pixels", 1, 150, start.BloomRadius, defaults.BloomRadius, (s, v) => s.BloomRadius = v, "0");
                break;
            case FilterKind.MotionBlur:
                Add(group, "Angle, degrees", -90, 90, start.MotionAngle, defaults.MotionAngle, (s, v) => s.MotionAngle = v);
                Add(group, "Distance, pixels", 1, 2000, start.MotionDistance, defaults.MotionDistance, (s, v) => s.MotionDistance = v, "0");
                break;
            case FilterKind.AddNoise:
                Add(group, "Amount, %", 0.1, 400, start.NoiseAmount, defaults.NoiseAmount, (s, v) => s.NoiseAmount = v);
                Check(group, "Gaussian", start.NoiseGaussian, (s, v) => s.NoiseGaussian = v);
                Check(group, "Monochromatic", start.NoiseMonochromatic, (s, v) => s.NoiseMonochromatic = v);
                break;
            case FilterKind.Vignette:
                // The colour is a swatch that opens the picker, as the Mac's sheet has it, where this panel
                // used to offer three numbers for it.
                Swatch(group, "Color", (start.VignetteRed, start.VignetteGreen, start.VignetteBlue),
                    (defaults.VignetteRed, defaults.VignetteGreen, defaults.VignetteBlue),
                    (s, colour) =>
                    {
                        s.VignetteRed = colour.Red;
                        s.VignetteGreen = colour.Green;
                        s.VignetteBlue = colour.Blue;
                    }, "Color Picker (Vignette Color)", "Choose the vignette color");
                Add(group, "Amount", 0, 100, start.VignetteAmount, defaults.VignetteAmount, (s, v) => s.VignetteAmount = v);
                Add(group, "Midpoint", 0, 100, start.VignetteMidpoint, defaults.VignetteMidpoint, (s, v) => s.VignetteMidpoint = v);
                Add(group, "Roundness", -100, 100, start.VignetteRoundness, defaults.VignetteRoundness, (s, v) => s.VignetteRoundness = v);
                Add(group, "Feather", 0, 100, start.VignetteFeather, defaults.VignetteFeather, (s, v) => s.VignetteFeather = v);
                Add(group, "Highlights", 0, 100, start.VignetteHighlights, defaults.VignetteHighlights, (s, v) => s.VignetteHighlights = v);
                break;
            case FilterKind.TonalContrast:
                Add(group, "Amount", 0, 100, start.TonalAmount, defaults.TonalAmount, (s, v) => s.TonalAmount = v);
                Add(group, "Radius, pixels", 1, 100, start.TonalRadius, defaults.TonalRadius, (s, v) => s.TonalRadius = v, "0");
                Add(group, "Shadows", -100, 100, start.TonalShadows, defaults.TonalShadows, (s, v) => s.TonalShadows = v);
                Add(group, "Midtones", -100, 100, start.TonalMidtones, defaults.TonalMidtones, (s, v) => s.TonalMidtones = v);
                Add(group, "Highlights", -100, 100, start.TonalHighlights, defaults.TonalHighlights, (s, v) => s.TonalHighlights = v);
                break;
            case FilterKind.RimLight:
                Height = 700; Width = 470;
                RimLightControls(group, start.RimLight);
                break;
            case FilterKind.VolumetricLight:
                Height = 700; Width = 540;
                var volumetric = new VolumetricLightSettings();
                group.Children.Add(new TextBlock { Text = Localize.Text("The light starts on the layer's biggest bright area; move it with Light X and Light Y"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Skin.SecondaryBrush, Margin = new Thickness(0, 0, 0, 4) });
                Swatch(group, "Color", (start.Volumetric.Red, start.Volumetric.Green, start.Volumetric.Blue), (volumetric.Red, volumetric.Green, volumetric.Blue),
                    (s, c) => { s.Volumetric.Red = c.Red; s.Volumetric.Green = c.Green; s.Volumetric.Blue = c.Blue; }, "Color Picker (Light Color)", "Choose the light's color");
                Add(group, "Light X (%)", -100, 200, start.Volumetric.CenterX, start.Volumetric.CenterX, (s, v) => s.Volumetric.CenterX = v);
                Add(group, "Light Y (%)", -100, 200, start.Volumetric.CenterY, start.Volumetric.CenterY, (s, v) => s.Volumetric.CenterY = v);
                Add(group, "Haze", 0, 100, start.Volumetric.Haze, volumetric.Haze, (s, v) => s.Volumetric.Haze = v, "0");
                Add(group, "Distance", 0, 100, start.Volumetric.Distance, volumetric.Distance, (s, v) => s.Volumetric.Distance = v, "0",
                    "How far into the scene the haze begins: low reaches up to the camera, high leaves all but the farthest parts clear");
                Add(group, "Depth Contrast", 0, 100, start.Volumetric.DepthContrast, volumetric.DepthContrast, (s, v) => s.Volumetric.DepthContrast = v, "0",
                    "Spread out depths bunched together, such as a distant city, so the haze builds through them in steps; 0 keeps the depth as the model sees it");
                Add(group, "Size", 0, 100, start.Volumetric.Size, volumetric.Size, (s, v) => s.Volumetric.Size = v, "0");
                Add(group, "Intensity", 0, 100, start.Volumetric.Intensity, volumetric.Intensity, (s, v) => s.Volumetric.Intensity = v, "0");
                Add(group, "Length", 0, 100, start.Volumetric.Length, volumetric.Length, (s, v) => s.Volumetric.Length = v, "0");
                Add(group, "Shafts", 0, 100, start.Volumetric.Shafts, volumetric.Shafts, (s, v) => s.Volumetric.Shafts = v, "0");
                break;
            default:
                Add(group, "Distortion", -100, 100, start.Distortion, defaults.Distortion, (s, v) => s.Distortion = v);
                break;
        }

        var ok = new Button
        {
            Content = Localize.Text("Apply"),
            IsDefault = true,
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.AccentBrush,
            BorderBrush = Skin.AccentBrush,
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var cancel = new Button
        {
            Content = Localize.Text("Cancel"),
            IsCancel = true,
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var reset = new Button
        {
            Content = Localize.Text("Reset"),
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();
        reset.Click += (_, _) => Restore();
        _preview.IsCheckedChanged += (_, _) => Preview?.Invoke(_preview.IsChecked == true ? Current() : null);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { _preview, new TextBlock { Text = Localize.Text(""), Width = 8 }, reset, cancel, ok },
        };
        if (kind == FilterKind.VolumetricLight)
        {
            // Extract Depth shares the row with Cancel and OK, and both wait while the depth model is fetched.
            _ok = ok;
            _extract = new Button
            {
                Content = Localize.Text("Extract Depth"), MinWidth = 96, Height = 26, FontSize = 12, CornerRadius = new CornerRadius(7),
                Background = Skin.SurfaceControlBrush, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1),
                Foreground = Skin.LabelBrush, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "extract-depth",
            };
            ToolTip.SetTip(_extract, Localize.Text("Close the filter and add the depth the haze goes by as a new layer: white is near, black is far"));
            _extract.Click += (_, _) => { _extracted = Current(); Close(); };
            buttons.Children.Insert(buttons.Children.IndexOf(cancel), _extract);
            _waiting = new TextBlock { Text = Localize.Text("Getting depth…"), FontSize = 11, Foreground = Skin.SecondaryBrush, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
            group.Children.Add(_waiting);
        }
        group.Children.Add(buttons);

        Content = new ScrollViewer { Content = group };
    }

    /// <summary>
    /// Rim Light: Glow, then each light's color, angle, strength, width and choke, with room for up to three. The
    /// panel keeps three slots and shows the ones in use; removing a light moves the ones after it up.
    /// </summary>
    private void RimLightControls(StackPanel group, RimLightSettings start)
    {
        Add(group, "Glow", 0, 100, start.Glow, new RimLightSettings().Glow, (s, v) => s.RimLight.Glow = v, "0");
        _rimCount = Math.Clamp(start.Lights.Count, 1, RimLightSettings.MaximumLights);
        // The panel's own copy holds all three, so a color picked for a slot not yet in use has somewhere to go.
        while (_amounts.RimLight.Lights.Count < RimLightSettings.MaximumLights) _amounts.RimLight.Lights.Add(_amounts.RimLight.NextLight);
        var fallback = new RimLightSettings();
        while (fallback.Lights.Count < RimLightSettings.MaximumLights) fallback.Lights.Add(fallback.NextLight);
        for (var slot = 0; slot < RimLightSettings.MaximumLights; slot++)
        {
            var k = slot;
            var light = _amounts.RimLight.Lights[k]; var origin = fallback.Lights[k];
            var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            var remove = new Button { Content = Localize.Text("Remove"), FontSize = 11, Height = 22, Padding = new Thickness(8, 0), CornerRadius = new CornerRadius(6) };
            ToolTip.SetTip(remove, Localize.Text("Remove this light"));
            remove.Click += (_, _) => RemoveLight(k);
            var header = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(remove, Dock.Right);
            header.Children.Add(remove);
            header.Children.Add(new TextBlock { Text = string.Format(Localize.Text("Light {0}"), k + 1), FontWeight = FontWeight.SemiBold, FontSize = 12, Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new Separator { Margin = new Thickness(0, 2) });
            panel.Children.Add(header);
            Swatch(panel, "Color", (light.Red, light.Green, light.Blue), (origin.Red, origin.Green, origin.Blue),
                (s, c) => { s.RimLight.Lights[k].Red = c.Red; s.RimLight.Lights[k].Green = c.Green; s.RimLight.Lights[k].Blue = c.Blue; },
                string.Format(Localize.Text("Color Picker (Light {0})"), k + 1), "Choose this light's color");
            var first = _rows.Count;
            Add(panel, "Angle, degrees", -180, 180, light.Angle, origin.Angle, (s, v) => s.RimLight.Lights[k].Angle = v, "0");
            Add(panel, "Strength", 0, 100, light.Strength, origin.Strength, (s, v) => s.RimLight.Lights[k].Strength = v, "0");
            Add(panel, "Width", 0, 100, light.Width, origin.Width, (s, v) => s.RimLight.Lights[k].Width = v, "0");
            Add(panel, "Choke", 0, 100, light.Choke, origin.Choke, (s, v) => s.RimLight.Lights[k].Choke = v, "0");
            _rimSlots.Add((panel, remove, first, _swatches[^1].Swatch));
            group.Children.Add(panel);
        }
        _addLight = new Button { Content = Localize.Text("Add Light"), FontSize = 12, Height = 26, Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(7) };
        ToolTip.SetTip(_addLight, Localize.Text("Light the subject from another side as well"));
        _addLight.Click += (_, _) => AddLight();
        group.Children.Add(_addLight);
        ShowLights();
    }

    /// <summary>Rim Light's slots as the panel has them: three, the first <see cref="_rimCount"/> in use.</summary>
    private int _rimCount;
    private readonly List<(StackPanel Panel, Button Remove, int FirstRow, ColorSwatch Swatch)> _rimSlots = [];
    private Button? _addLight;

    private void ShowLights()
    {
        for (var k = 0; k < _rimSlots.Count; k++)
        {
            _rimSlots[k].Panel.IsVisible = k < _rimCount;
            _rimSlots[k].Remove.IsVisible = _rimCount > 1;
        }
        if (_addLight is not null) _addLight.IsVisible = _rimCount < RimLightSettings.MaximumLights;
    }

    /// <summary>A light from the side across from the ones there, in a color that sets them off.</summary>
    internal void AddLight()
    {
        if (_rimCount >= RimLightSettings.MaximumLights) return;
        var next = Current().RimLight.NextLight;
        var (_, _, first, swatch) = _rimSlots[_rimCount];
        _rows[first].Slider.Value = next.Angle; _rows[first + 1].Slider.Value = next.Strength;
        _rows[first + 2].Slider.Value = next.Width; _rows[first + 3].Slider.Value = next.Choke;
        swatch.Colour = (next.Red, next.Green, next.Blue);
        _rimCount++;
        ShowLights();
        Preview?.Invoke(_preview.IsChecked == true ? Current() : null);
    }

    /// <summary>The light in <paramref name="slot"/> taken away, the ones after it moved up.</summary>
    internal void RemoveLight(int slot)
    {
        if (_rimCount <= 1 || slot >= _rimCount) return;
        for (var k = slot; k < _rimCount - 1; k++)
        {
            var (_, _, to, toSwatch) = _rimSlots[k]; var (_, _, from, fromSwatch) = _rimSlots[k + 1];
            for (var row = 0; row < 4; row++) _rows[to + row].Slider.Value = _rows[from + row].Slider.Value;
            toSwatch.Colour = fromSwatch.Colour;
        }
        _rimCount--;
        ShowLights();
        Preview?.Invoke(_preview.IsChecked == true ? Current() : null);
    }

    /// <summary>A box that follows the setting it belongs to, and what it started as for Reset.</summary>
    private void Check(StackPanel parent, string label, bool value, Action<FilterSettings, bool> set)
    {
        var box = new CheckBox { Content = Localize.Text(label), IsChecked = value, FontSize = 12, Foreground = Skin.LabelBrush };
        parent.Children.Add(box);
        _checks.Add((box, set, value));
    }

    private void Add(StackPanel parent, string label, double least, double most, double value, double fallback,
        Action<FilterSettings, double> set, string format = "0.#", string? hint = null)
    {
        var slider = new Slider { Minimum = least, Maximum = most, Value = value, Width = 240 };
        if (hint is not null) ToolTip.SetTip(slider, Localize.Text(hint));
        var readout = new TextBlock { Text = Localize.Text(""), Width = 44, FontSize = 11, Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
        void Show() => readout.Text = slider.Value.ToString(format);
        slider.PropertyChanged += (_, change) =>
        {
            if (change.Property != Slider.ValueProperty) return;
            Show();
            Preview?.Invoke(Current());
        };
        Show();
        parent.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Localize.Text(label), Width = 130, FontSize = 12, Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center },
                slider,
                readout,
            },
        });
        _rows.Add((slider, set));
        _fallbacks.Add(fallback);
    }

    /// <summary>
    /// A colour the panel offers as a swatch, which opens the app's picker on it. What the picker reports is
    /// written into the amounts and previewed, so the picture follows the colour while it is being chosen; a
    /// Cancel reports the colour it opened on and puts both back.
    /// </summary>
    private void Swatch(StackPanel parent, string label, (double Red, double Green, double Blue) colour,
        (double Red, double Green, double Blue) fallback,
        Action<FilterSettings, (double Red, double Green, double Blue)> set, string title, string hint)
    {
        var swatch = new ColorSwatch(hint) { Colour = colour };
        swatch.Click += (_, _) => _ = ColorPickerDialog.Pick(this, title, swatch.Colour, moved =>
        {
            swatch.Colour = moved;
            set(_amounts, moved);
            Preview?.Invoke(Current());
        });
        parent.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Localize.Text(label), Width = 130, VerticalAlignment = VerticalAlignment.Center },
                swatch,
            },
        });
        _swatches.Add((swatch, set, fallback));
    }

    /// <summary>Back to the filter's own defaults, which for a vignette is not zero.</summary>
    private void Choice(StackPanel group, string label, string[] values, int value, int fallback, Action<FilterSettings,int> set)
    {
        group.Children.Add(new TextBlock { Text=Localize.Text(label) });
        var box=new ComboBox { ItemsSource=values.Select(Localize.Text).ToArray(), SelectedIndex=value, HorizontalAlignment=HorizontalAlignment.Stretch };
        group.Children.Add(box); _choices.Add((box,fallback,set)); box.SelectionChanged+=(_,_)=>Preview?.Invoke(_preview.IsChecked==true ? Current() : null);
    }
    private void Restore()
    {
        for (var i = 0; i < _rows.Count; i++) _rows[i].Slider.Value = _fallbacks[i];
        foreach (var (box, _, fallback) in _checks) box.IsChecked = fallback;
        foreach(var (box, fallback, _) in _choices) box.SelectedIndex=fallback;
        foreach (var (swatch, set, fallback) in _swatches)
        {
            swatch.Colour = fallback;
            set(_amounts, fallback);
        }
        if (_rimSlots.Count > 0) { _rimCount = 1; ShowLights(); }
    }

    /// <summary>The amounts as the panel has them, for a preview of what they would do. The self check reads
    /// them to see what its own clicks did.</summary>
    internal FilterSettings Current()
    {
        var settings = _amounts.Copy();
        foreach(var (box, _, set) in _choices) if(box.SelectedIndex>=0) set(settings,box.SelectedIndex);
        foreach (var (slider, set) in _rows) set(settings, slider.Value);
        foreach (var (box, set, _) in _checks) set(settings, box.IsChecked == true);
        foreach (var (swatch, set, _) in _swatches) set(settings, swatch.Colour);
        if (_rimSlots.Count > 0) settings.RimLight.Lights.RemoveRange(_rimCount, settings.RimLight.Lights.Count - _rimCount);
        return settings;
    }

    private void Accept()
    {
        _result = Current();
        Close();
    }

    private Button? _ok, _extract;
    private TextBlock? _waiting;
    private FilterSettings? _extracted;

    /// <summary>
    /// Volumetric Light's first opening, while the depth model downloads (about 27 MB): Extract Depth and OK wait for it,
    /// the line under the sliders says what for, and Cancel stays. Once it's here the preview is drawn again with it.
    /// </summary>
    internal void WaitFor(Task<bool> depth)
    {
        if (_ok is null || _extract is null || _waiting is null || depth.IsCompleted) return;
        _ok.IsEnabled = _extract.IsEnabled = false; _waiting.IsVisible = true;
        depth.ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ok.IsEnabled = _extract.IsEnabled = true; _waiting.IsVisible = false;
            Preview?.Invoke(_preview.IsChecked == true ? Current() : null);
        }), TaskScheduler.Default);
    }

    /// <summary>The amounts to apply, or null when the panel was dismissed or asks for nothing. Volumetric Light's
    /// Extract Depth closes the panel and hands its amounts to <paramref name="extractDepth"/> instead.</summary>
    public static async Task<FilterSettings?> Ask(Window owner, FilterKind kind, FilterSettings start,
        Action<FilterSettings?>? preview = null, Task<bool>? depth = null, Action<FilterSettings>? extractDepth = null)
    {
        var dialog = new FilterDialog(kind, start) { Preview = preview };
        if (depth is not null) dialog.WaitFor(depth);
        await dialog.ShowDialog(owner);
        if (dialog._extracted is { } extracted) { extractDepth?.Invoke(extracted); return null; }
        return dialog._result is { } settings && settings.IsValid(kind) && settings.DoesAnything(kind) ? settings : null;
    }
}
