namespace Compositor.Core.Document;
public sealed record BuiltInBrush(string Name, string Category, BrushSettings Settings);
/// <summary>Original procedural presets, with stroke previews rendered from these same settings.</summary>
public static class BrushPresetLibrary
{
    public static IReadOnlyList<BuiltInBrush> All { get; } = Build();
    private static BuiltInBrush[] Build()
    {
        var brushes = new List<BuiltInBrush>();
        for (var mode = 0; mode < 4; mode++) for (var hard = 0; hard < 2; hard++)
        {
            var suffix = new[] { "", " Pressure Size", " Pressure Opacity", " Pressure Opacity and Flow" }[mode];
            brushes.Add(new((hard == 0 ? "Soft Round" : "Hard Round") + suffix, "General Brushes", new(Hardness: hard, SpacingFraction: .12, Painting: new() { Tip = hard, ShapeEnabled = mode == 1, MinimumDiameter = .15, TransferEnabled = mode >= 2, OpacityControl = mode >= 2 ? BrushControl.PenPressure : BrushControl.Off, FlowControl = mode == 3 ? BrushControl.PenPressure : BrushControl.Off })));
        }
        void Add(string name, string group, int tip, double spacing, PaintBrushOptions? options = null, double hardness = 1, double roundness = 1)
        { brushes.Add(new(name, group, new(Hardness: hardness, Roundness: roundness, SpacingFraction: spacing, Painting: (options ?? new()) with { Tip = tip }))); }
        Add("Graphite Pencil", "Dry Media Brushes", 10, .08, new() { TextureEnabled = true, TextureDepth = .8, ShapeEnabled = true, MinimumDiameter = .5 }, roundness: .3);
        Add("Drawing Pencil", "Dry Media Brushes", 5, .09, new() { Noise = true, ShapeEnabled = true, MinimumDiameter = .4 }, roundness: .4);
        Add("Soft Charcoal", "Dry Media Brushes", 16, .12, new() { AngleJitter = .15, ShapeEnabled = true, SizeJitter = .1 });
        Add("Sparse Charcoal", "Dry Media Brushes", 15, .2, new() { ScatterEnabled = true, Scatter = .1, Noise = true });
        Add("Pastel", "Dry Media Brushes", 5, .15, new() { TextureEnabled = true, TextureDepth = .9, SizeJitter = .15, ShapeEnabled = true });
        Add("Natural Edge", "Dry Media Brushes", 6, .1, new() { Noise = true }, roundness: .6);
        Add("Broad Ink", "Wet Media Brushes", 8, .08, new() { ShapeEnabled = true, MinimumDiameter = .35 });
        Add("Watercolor Line", "Wet Media Brushes", 10, .08, new() { WetEdges = true, TransferEnabled = true, Flow = .3 }, roundness: .4);
        Add("Wet Wash", "Wet Media Brushes", 9, .2, new() { WetEdges = true, TransferEnabled = true, Flow = .35 }, hardness: .1);
        Add("Oil Bristles", "Wet Media Brushes", 10, .08, new() { ShapeEnabled = true, MinimumDiameter = .7, TextureEnabled = true, Texture = 1 });
        Add("Flat Oil", "Wet Media Brushes", 12, .08, new() { AngleControl = BrushControl.Direction, ShapeEnabled = true }, roundness: .45);
        Add("Impression Dabs", "Wet Media Brushes", 23, .7, new() { ScatterEnabled = true, Scatter = .4, ShapeEnabled = true, AngleJitter = .5, SizeJitter = .3 });
        Add("Fine Spatter", "Special Effects Brushes", 17, .25, new() { ScatterEnabled = true, Scatter = .7, Count = 2 });
        Add("Textured Spatter", "Special Effects Brushes", 3, .3, new() { ScatterEnabled = true, Scatter = .8, TextureEnabled = true });
        Add("Pressure Spatter", "Special Effects Brushes", 15, .3, new() { ScatterEnabled = true, Scatter = .8, ShapeEnabled = true, MinimumDiameter = .1 });
        Add("Cloud Texture", "Special Effects Brushes", 9, .2, new() { ScatterEnabled = true, Scatter = .35, ShapeEnabled = true, SizeJitter = .4 });
        Add("Scratch Texture", "Special Effects Brushes", 13, .13, new() { TextureEnabled = true, TextureDepth = .7 }, roundness: .7);
        Add("Leaf Scatter", "Special Effects Brushes", 4, .8, new() { ScatterEnabled = true, Scatter = .65, Count = 2, ShapeEnabled = true, SizeJitter = .4, AngleJitter = 1 });
        Add("Halftone Rings", "Special Effects Brushes", 19, .65, new() { ScatterEnabled = true, Scatter = .6, Count = 3 });
        Add("Halftone Dots", "Special Effects Brushes", 21, .6, new() { ScatterEnabled = true, Scatter = .5, Count = 2 });
        return brushes.ToArray();
    }
}
