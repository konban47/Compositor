using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private sealed class MaskWorkspace(CanvasDocument document, Guid layer, Guid revision, MaskRefinementSettings settings)
    {
        public CanvasDocument Document = document;
        public Guid Layer = layer;
        public Guid Revision = revision;
        public MaskRefinementSettings Settings = settings;
        public bool Original, Live = true, Edges, HighQuality = true, Remember;
        public double Opacity = 20;
        public int View, Output;
        public CanvasDocument? Preview;
        public SKBitmap? Alpha, PreviousAlpha;
        public ColorChannels PreviousChannels;
    }
    private MaskWorkspace? _maskWorkspace;
    private Task RefineMask()
    {
        if (_document is not { } doc || PropertyLayer is not { Mask: { } mask } layer) return Task.CompletedTask;
        CloseMaskWorkspace();
        _maskWorkspace = new(doc, layer.ID, _history.CurrentRevision,
            EditorPreferences.Current.MaskRefinementPreset ?? new(Feather: mask.Feather));
        _maskWorkspace.PreviousAlpha = _canvas.AlphaDisplay; _maskWorkspace.PreviousChannels = _canvas.DisplayChannels;
        BuildProperties(); OpenInspector(0); PreviewMaskWorkspace(); return Task.CompletedTask;
    }
    private bool BuildMaskWorkspace()
    {
        if (_maskWorkspace is not { } state) return false;
        if (!ReferenceEquals(state.Document, _document) || state.Revision != _history.CurrentRevision)
        { CloseMaskWorkspace(); return false; }
        var settings = state.Settings; PropertyHeading("Select and Mask", "mask");
        CheckBox Check(string name, bool value, Action<bool> change)
        {
            var field = new CheckBox { Content = Localize.Text(name), IsChecked = value, Tag = name, MinHeight = 24 };
            field.IsCheckedChanged += (_, _) => { change(field.IsChecked == true); PreviewMaskWorkspace(); }; return field;
        }
        Control Amount(string name, double value, double max, Action<double> change, double min = 0)
        {
            var number = PropertyNumber("refine-" + name, value, min, max, v => { change(v); PreviewMaskWorkspace(); });
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, Tag = "refine-slider-" + name };
            slider.PointerReleased += (_, _) => { number.Value = (decimal)slider.Value; change(slider.Value); PreviewMaskWorkspace(); };
            slider.KeyUp += (_, _) => { number.Value = (decimal)slider.Value; change(slider.Value); PreviewMaskWorkspace(); };
            return new StackPanel { Spacing = 2, Children = { PropertyPair(PropertyLabel(name), number), slider } };
        }
        Section("View Mode", PropertyChoice("Mask View", ["On Layers", "Overlay", "On Black", "On White", "Black and White"], state.View, v => { state.View = v; PreviewMaskWorkspace(); }),
            Check("Show Edge", state.Edges, v => state.Edges = v), Check("Show Original", state.Original, v => state.Original = v),
            Check("Real-time Refinement", state.Live, v => state.Live = v), Check("High Quality Preview", state.HighQuality, v => state.HighQuality = v),
            Amount("Transparency (%)", state.Opacity, 100, v => state.Opacity = v));
        Section("Preset", PropertyChoice("Refinement Preset", ["Default", "Soft Edge", "Hard Edge", "Remembered"], 0, v =>
            { state.Settings = v switch { 1 => new(Radius: 2, SmartRadius: true, Feather: 1), 2 => new(Contrast: 50), 3 => EditorPreferences.Current.MaskRefinementPreset ?? new(), _ => new() }; BuildProperties(); PreviewMaskWorkspace(); }),
            Check("Remember Settings", state.Remember, v => state.Remember = v));
        Section("Refine Mode", PropertyChoice("Refine Mode", ["Color Aware", "Object Aware"], settings.ObjectAware ? 1 : 0, v => { state.Settings = state.Settings with { ObjectAware = v == 1 }; PreviewMaskWorkspace(); }));
        Section("Edge Detection", Amount("Radius (px)", settings.Radius, 100, v => state.Settings = state.Settings with { Radius = v }),
            Check("Smart Radius", settings.SmartRadius, v => state.Settings = state.Settings with { SmartRadius = v }));
        Section("Global Refinements", Amount("Smooth (px)", settings.Smooth, 100, v => state.Settings = state.Settings with { Smooth = v }),
            Amount("Feather (px)", settings.Feather, 1000, v => state.Settings = state.Settings with { Feather = v }),
            Amount("Contrast (%)", settings.Contrast, 100, v => state.Settings = state.Settings with { Contrast = v }),
            Amount("Shift Edge (%)", settings.ShiftPercent, 100, v => state.Settings = state.Settings with { ShiftPercent = v }, -100),
            PropertyPair(PropertyAction("Clear Selection", () => { state.Settings = state.Settings with { Clear = true }; PreviewMaskWorkspace(); }),
                PropertyAction("Invert", () => { state.Settings = state.Settings with { Invert = !state.Settings.Invert }; PreviewMaskWorkspace(); })));
        Section("Output Settings", Check("Decontaminate Colors", settings.Decontaminate, v => state.Settings = state.Settings with { Decontaminate = v }),
            Amount("Amount (%)", settings.Amount, 100, v => state.Settings = state.Settings with { Amount = v }),
            PropertyChoice("Output To", ["Layer Mask", "Selection", "New Layer", "New Layer with Layer Mask"], state.Output, v => state.Output = v));
        _properties.Children.Add(PropertyPair(PropertyAction("Preview", () => PreviewMaskWorkspace(true)), PropertyAction("Reset", () => { state.Settings = new(); BuildProperties(); PreviewMaskWorkspace(); })));
        _properties.Children.Add(PropertyPair(PropertyAction("OK", ApplyMaskWorkspace), PropertyAction("Cancel", () => { CloseMaskWorkspace(); BuildProperties(); })));
        return true;
    }
    private void PreviewMaskWorkspace(bool force = false)
    {
        if (_maskWorkspace is not { } state || !ReferenceEquals(state.Document, _document)) return;
        if (!state.Live && !force) return;
        var draft = state.Document.Clone();
        if (!state.Original) MaskRefinement.Apply(draft, state.Layer, state.Settings);
        DisposeMaskPreview(state); state.Preview = draft; _canvas.PreviewDocument = draft;
        state.Alpha?.Dispose(); state.Alpha = null;
        _canvas.AlphaDisplay = null; _canvas.DisplayChannels = ColorChannels.RGB;
        var layer = draft.Layers.First(l => l.ID == state.Layer);
        if (state.View is 1 or 4 || state.Edges)
        {
            state.Alpha = MaskProperties.Coverage(layer.Mask!, layer.MaskTransform, new Compositor.Core.Model.LayerTransform(0, 0, draft.Width, draft.Height), draft.Width, draft.Height);
            if (state.Edges)
            {
                using var copy = state.Alpha.Copy(); var source = copy.GetPixelSpan(); var span = state.Alpha.GetPixelSpan();
                for (var y = 0; y < copy.Height; y++) for (var x = 0; x < copy.Width; x++)
                { var a = source[y * copy.RowBytes + x]; var b = source[y * copy.RowBytes + Math.Min(x + 1, copy.Width - 1)]; var c = source[Math.Min(y + 1, copy.Height - 1) * copy.RowBytes + x]; span[y * state.Alpha.RowBytes + x] = (byte)(Math.Abs(a - b) + Math.Abs(a - c) > 20 ? 0 : 255); }
            }
            _canvas.AlphaDisplay = state.Alpha; _canvas.DisplayChannels = state.View == 4 ? ColorChannels.None : ColorChannels.RGB;
        }
        if (state.View is 2 or 3)
        {
            var visible = draft.Descendants(state.Layer); visible.Add(state.Layer);
            var ancestor = layer.ParentID; while (ancestor is { } parent) { visible.Add(parent); ancestor = draft.Layers.FirstOrDefault(l => l.ID == parent)?.ParentID; }
            foreach (var other in draft.Layers) other.IsVisible = visible.Contains(other.ID);
            draft.Image = draft.Image with { Background = state.View == 2 ? 0xFF000000u : 0xFFFFFFFFu };
        }
        foreach (var other in draft.Layers) other.Transform = other.Transform with { Sampling = state.HighQuality ? Compositor.Core.Format.LayerSampling.HighQuality : Compositor.Core.Format.LayerSampling.Nearest };
        _canvas.RefinementOpacity = 1 - state.Opacity / 100;
        _canvas.InvalidateVisual();
    }
    private void ApplyMaskWorkspace()
    {
        if (_maskWorkspace is not { } state || !ReferenceEquals(state.Document, _document) || state.Revision != _history.CurrentRevision) return;
        CloseMaskWorkspace(); var id = state.Layer;
        Edit("Select and Mask", () =>
        {
            if (state.Output >= 2) { if (LayerEdits.Duplicate(state.Document, id) is not { } copy) return false; id = copy; }
            if (state.Output == 1)
            {
                using var selected = state.Document.Clone();
                if (!MaskRefinement.Apply(selected, id, state.Settings) || !MaskProperties.LoadSelection(selected, id)) return false;
                state.Document.Selection = selected.Selection; var holder = new MaskWorkspace(state.Document, id, state.Revision, state.Settings) { Preview = selected }; DisposeMaskPreview(holder); return true;
            }
            if (!MaskRefinement.Apply(state.Document, id, state.Settings)) return false;
            if (state.Output == 2) return MaskProperties.Apply(state.Document, id);
            return true;
        });
        if (state.Remember) { EditorPreferences.Current.MaskRefinementPreset = state.Settings; SavePreferenceChanges(); }
        Reselect(id); BuildProperties();
    }
    private static void DisposeMaskPreview(MaskWorkspace state)
    {
        if (state.Preview is not { } preview) return;
        var shared = state.Document.Layers.SelectMany(l => new[] { l.Asset, l.Mask?.Asset }).Where(a => a is not null).ToHashSet();
        foreach (var asset in preview.Layers.SelectMany(l => new[] { l.Asset, l.Mask?.Asset }).Where(a => a is not null && !shared.Contains(a)).Distinct()) asset!.Dispose();
        preview.Dispose(); state.Preview = null;
    }
    private void CloseMaskWorkspace()
    {
        _canvas.PreviewDocument = null; _canvas.RefinementOpacity = null;
        if (_maskWorkspace is { } state) { _canvas.AlphaDisplay = state.PreviousAlpha; _canvas.DisplayChannels = state.PreviousChannels; state.Alpha?.Dispose(); }
        if (_maskWorkspace is { } previous) DisposeMaskPreview(previous); _maskWorkspace = null; _canvas.InvalidateVisual();
    }
}
