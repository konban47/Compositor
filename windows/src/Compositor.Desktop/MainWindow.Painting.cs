using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private bool PaintingTool => _tool is Tool.Brush or Tool.Pencil or Tool.ColorReplacement or Tool.MixerBrush or Tool.Clone or Tool.PatternStamp or Tool.HistoryBrush or Tool.ArtHistory or Tool.Eraser or Tool.BackgroundEraser or Tool.Blur or Tool.Sharpen or Tool.Smudge or Tool.Dodge or Tool.Burn or Tool.Sponge or Tool.AdjustmentBrush;
    private PaintBrushOptions PaintingOptions => _options.Brush.Painting ?? new();
    private MixerReservoir _mixerReservoir = new(SKColors.Black, 1);
    private void ChangePainting(Func<PaintBrushOptions, PaintBrushOptions> change)
    { _options.Brush = _options.Brush with { Painting = change(PaintingOptions) }; OptionsChanged(); _canvas.InvalidateOverlay(); _brushSettingsEditor?.UpdatePreview(); }
    private void PrepareMixerStroke()
    {
        if (PaintingOptions.MixerAutoLoad) _mixerReservoir = new(new SKColor(PaintingOptions.MixerColor), _options.Brush.MixerLoad);
        PushBrush();
    }
    private void FinishMixerStroke()
    {
        if (PaintingOptions.MixerAutoClean) _mixerReservoir = new(SKColors.Transparent, 0);
        if (PaintingOptions.MixerAutoLoad) _mixerReservoir = new(new SKColor(PaintingOptions.MixerColor), _options.Brush.MixerLoad);
        ChangePainting(p => p with { MixerLoaded = _mixerReservoir.Amount > 0 });
    }
    private void LoadMixer(SKPoint? at = null)
    {
        var color = BrushColour(); string? image = null;
        if (at is { } point && _document is { } doc && Selected is { } id)
        {
            if (PaintingOptions.MixerSolidOnly) color = ColorSampling.Read(doc, id, point, 1, PaintingOptions.MixerAllLayers ? SampleScope.AllLayers : SampleScope.CurrentLayer);
            else
            {
                var size = Math.Clamp((int)Math.Ceiling(_options.Brush.Diameter), 1, 512);
                var region = SKRectI.Intersect(SKRectI.Create((int)point.X - size / 2, (int)point.Y - size / 2, size, size), SKRectI.Create(doc.Width, doc.Height));
                if (!region.IsEmpty)
                {
                    using var view = ColorSampling.View(doc, id, PaintingOptions.MixerAllLayers ? SampleScope.AllLayers : SampleScope.CurrentLayer);
                    using var pixels = DocumentRenderer.RenderRegion(view, region);
                    color = pixels.GetPixel(pixels.Width / 2, pixels.Height / 2); image = Convert.ToBase64String(PngCodec.Encode(pixels));
                }
            }
        }
        _mixerReservoir = new(color, _options.Brush.MixerLoad);
        ChangePainting(p => p with { MixerColor = (uint)color, MixerImage = image, MixerLoaded = true });
    }
    private async Task PaintingAction(string action, Control anchor)
    {
        try
        {
            if (action == "Gradient Presets") { OpenGradientPicker(anchor); return; }
            if (action == "Gradient Editor") { await EditGradientStops(); return; }
            if (HandleViewAction(action)) return;
            if (action == "Adjustment Settings") { await EditAdjustment(); return; }
            if (action == "Apply Adjustment to Object") { await ApplyAdjustmentObject(); return; }
            if (action == "Pattern Presets") { OpenPatternPicker(anchor); return; }
            if (action == "Clone Source") { OpenUtilityPanel("Clone Source"); return; }
            if (action == "History Brush Source") { await SetHistoryBrushSource(); return; }
            if (action == "Brush Settings") { OpenUtilityPanel("Brush Settings"); return; }
            if (action == "Brush Tip Presets") { OpenBrushPicker(anchor); return; }
            if (action == "Paint Symmetry") { OpenPaintSymmetry(anchor); return; }
            if (action == "Mixer Load Menu")
            {
                var menu = new ContextMenu(); menu.Items.Add(Command("Load Brush", () => LoadMixer())); menu.Items.Add(Command("Clean Brush", () => { _mixerReservoir = new(SKColors.Transparent, 0); ChangePainting(p => p with { MixerLoaded = false, MixerImage = null }); }));
                var solid = Command("Load Solid Colors Only", () => ChangePainting(p => p with { MixerSolidOnly = !p.MixerSolidOnly })); solid.ToggleType = MenuItemToggleType.CheckBox; solid.IsChecked = PaintingOptions.MixerSolidOnly; menu.Items.Add(new Separator()); menu.Items.Add(solid); menu.Open(anchor); return;
            }
            var popup = new ToolSettingsPopup { Changed = () => { OptionsChanged(); _brushSettingsEditor?.UpdatePreview(); } };
            void Slider(string title, double value, Action<double> set) => popup.Number(title, value, 0, 100, set, true);
            switch (action)
            {
                case "Retouch Strength Slider": Slider("Strength:", _options.Brush.Strength * 100, n => _options.Brush = _options.Brush with { Strength = n / 100 }); break;
                case "Magic Tolerance Slider": popup.Number("Tolerance:", _options.Wand.Tolerance, 0, 255, n => _options.Wand = _options.Wand with { Tolerance = (int)n }, true); break;
                case "Art Tolerance Slider": Slider("Tolerance:", PaintingOptions.ArtTolerance * 100, n => ChangePainting(p => p with { ArtTolerance = n / 100 })); break;
                case "Paint Opacity Slider": Slider("Opacity:", _options.Brush.Opacity * 100, n => _options.Brush = _options.Brush with { Opacity = n / 100 }); break;
                case "Paint Flow Slider": Slider("Flow:", PaintingOptions.Flow * 100, n => ChangePainting(p => p with { Flow = n / 100 })); break;
                case "Paint Smoothing Slider": Slider("Smoothing:", _options.Brush.Smoothing * 100, n => _options.Brush = _options.Brush with { Smoothing = n / 100 }); break;
                case "Replace Tolerance Slider": Slider("Tolerance:", _options.Brush.Tolerance / 2.55, n => _options.Brush = _options.Brush with { Tolerance = n * 2.55 }); break;
                case "Mixer Wet Slider": Slider("Wet:", _options.Brush.MixerWet * 100, n => { _options.Brush = _options.Brush with { MixerWet = n / 100 }; ChangePainting(p => p with { MixerPreset = 0 }); }); break;
                case "Mixer Load Slider": Slider("Load:", _options.Brush.MixerLoad * 100, n => { _options.Brush = _options.Brush with { MixerLoad = n / 100 }; ChangePainting(p => p with { MixerPreset = 0 }); }); break;
                case "Mixer Mix Slider": Slider("Mix:", _options.Brush.MixerMix * 100, n => { _options.Brush = _options.Brush with { MixerMix = n / 100 }; ChangePainting(p => p with { MixerPreset = 0 }); }); break;
                case "Mixer Flow Slider": Slider("Flow:", _options.Brush.MixerFlow * 100, n => _options.Brush = _options.Brush with { MixerFlow = n / 100 }); break;
                case "Smoothing Options":
                    popup.Heading("Smoothing Options"); popup.Check("Pulled String Mode", PaintingOptions.PulledString, v => ChangePainting(p => p with { PulledString = v }));
                    popup.Check("Stroke Catch Up", PaintingOptions.CatchUp, v => ChangePainting(p => p with { CatchUp = v }));
                    popup.Check("Catch-up on Stroke End", PaintingOptions.CatchUpOnRelease, v => ChangePainting(p => p with { CatchUpOnRelease = v }));
                    popup.Check("Adjust for Zoom", PaintingOptions.AdjustForZoom, v => ChangePainting(p => p with { AdjustForZoom = v })); break;
                default: return;
            }
            popup.Show(anchor); await Task.CompletedTask;
        }
        catch (Exception error) { Say(error.Message); }
    }
    private void OpenPaintSymmetry(Control anchor)
    {
        var menu = new ContextMenu();
        void Add(string name, Action action, bool enabled = true, bool selected = false) { var item = Command(name, action); item.IsEnabled = enabled; item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = selected; menu.Items.Add(item); }
        void Choose(BrushSymmetry type) { ChangePainting(p => p with { Symmetry = type, LastSymmetry = type == BrushSymmetry.Off ? p.LastSymmetry : type, SymmetryVisible = true, SymmetryPath = type == BrushSymmetry.SelectedPath ? _document?.WorkPath : p.SymmetryPath }); }
        Add("Symmetry Off", () => Choose(BrushSymmetry.Off), selected: PaintingOptions.Symmetry == BrushSymmetry.Off);
        Add("Last Used Symmetry", () => Choose(PaintingOptions.LastSymmetry)); menu.Items.Add(new Separator());
        foreach (var type in Enum.GetValues<BrushSymmetry>().Where(t => t is not (BrushSymmetry.Off or BrushSymmetry.SelectedPath)))
            Add(BrushSettingsEditor.SplitName(type.ToString()), () => { Choose(type); if (type is BrushSymmetry.Radial or BrushSymmetry.Mandala) _ = TransformPaintSymmetry(); }, selected: PaintingOptions.Symmetry == type);
        menu.Items.Add(new Separator()); Add("Selected Path", () => Choose(BrushSymmetry.SelectedPath), _document?.WorkPath is not null);
        Add("Transform Symmetry…", () => _ = TransformPaintSymmetry(), PaintingOptions.Symmetry != BrushSymmetry.Off);
        Add("Hide Symmetry", () => ChangePainting(p => p with { SymmetryVisible = !p.SymmetryVisible }), PaintingOptions.Symmetry != BrushSymmetry.Off, !PaintingOptions.SymmetryVisible); menu.Open(anchor);
    }
    private async Task TransformPaintSymmetry()
    {
        if (_document is not { } doc) return; var o = PaintingOptions; var form = new FormDialog("Transform Symmetry", 400, 420);
        var x = form.Number("X", o.SymmetryX * doc.Width, -doc.Width * 2, doc.Width * 3); var y = form.Number("Y", o.SymmetryY * doc.Height, -doc.Height * 2, doc.Height * 3);
        var angle = form.Number("Angle", o.SymmetryAngle, -180, 180); var scale = form.Number("Scale %", o.SymmetryScale * 100, 1, 1000); var segments = form.Number("Segments", o.SymmetrySegments, 2, 12);
        if (await form.ShowDialog<bool>(this)) ChangePainting(p => p with { SymmetryX = (double)(x.Value ?? 0) / doc.Width, SymmetryY = (double)(y.Value ?? 0) / doc.Height, SymmetryAngle = (double)(angle.Value ?? 0), SymmetryScale = (double)(scale.Value ?? 100) / 100, SymmetrySegments = (int)(segments.Value ?? 5) });
    }
    private void DrawPaintSymmetry(DrawingContext context, Func<SKPoint, Point> screen)
    {
        if (_tool is not (Tool.Brush or Tool.Pencil or Tool.Eraser) || !PaintingOptions.SymmetryVisible || PaintingOptions.Symmetry == BrushSymmetry.Off || _document is not { } doc) return;
        var o = PaintingOptions; var center = new SKPoint((float)(o.SymmetryX * doc.Width), (float)(o.SymmetryY * doc.Height)); var radius = Math.Min(doc.Width, doc.Height) * .25 * o.SymmetryScale; var pen = new Pen(Skin.AccentBrush, 1);
        SKPoint Point(double x, double y) { var a = o.SymmetryAngle * Math.PI / 180; return center + new SKPoint((float)(x * Math.Cos(a) - y * Math.Sin(a)), (float)(x * Math.Sin(a) + y * Math.Cos(a))); }
        void Line(double x1, double y1, double x2, double y2) => context.DrawLine(pen, screen(Point(x1, y1)), screen(Point(x2, y2)));
        var span = Math.Max(doc.Width, doc.Height) * 2;
        if (o.Symmetry is BrushSymmetry.Vertical or BrushSymmetry.DualAxis or BrushSymmetry.Parallel) Line(0, -span, 0, span);
        if (o.Symmetry is BrushSymmetry.Horizontal or BrushSymmetry.DualAxis) Line(-span, 0, span, 0);
        if (o.Symmetry == BrushSymmetry.Diagonal) Line(-span, -span, span, span);
        if (o.Symmetry == BrushSymmetry.Parallel) Line(radius, -span, radius, span);
        if (o.Symmetry == BrushSymmetry.Circle) for (var i = 0; i < 128; i++) Line(Math.Cos(i * Math.PI / 64) * radius, Math.Sin(i * Math.PI / 64) * radius, Math.Cos((i + 1) * Math.PI / 64) * radius, Math.Sin((i + 1) * Math.PI / 64) * radius);
        if (o.Symmetry is BrushSymmetry.Radial or BrushSymmetry.Mandala) for (var i = 0; i < o.SymmetrySegments; i++) Line(0, 0, Math.Cos(i * Math.PI * 2 / o.SymmetrySegments) * span, Math.Sin(i * Math.PI * 2 / o.SymmetrySegments) * span);
        var guide = new BrushSymmetryMap(o, doc.Width, doc.Height).Guide; for (var i = 1; i < guide.Count; i++) context.DrawLine(pen, screen(guide[i - 1]), screen(guide[i]));
    }
}
