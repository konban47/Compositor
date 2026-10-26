using System.Collections.Immutable;
using System.Text.Json;
using Compositor.Core.Format;
using Compositor.Core.Model;
using Transform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Document;

[Flags]
public enum CompCapture { Visibility = 1, Position = 2, Appearance = 4, All = 7 }

/// <summary>Property snapshots only: comps never replace pixels, layer identities or the layer stack.</summary>
public sealed record LayerCompState(Guid LayerID, bool Visible, Transform Transform, double Opacity,
    double FillOpacity, LayerBlendMode BlendMode, LayerEffects? Effects, LayerBlending? Blending)
{
    public bool IsValid => LayerID != Guid.Empty && Transform.IsValid && double.IsFinite(Opacity) && Opacity is >= 0 and <= 1
        && double.IsFinite(FillOpacity) && FillOpacity is >= 0 and <= 1 && Enum.IsDefined(BlendMode)
        && (Effects?.IsValid ?? true) && (Blending?.IsValid ?? true);
}

public sealed record LayerComp
{
    public Guid ID { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Layer Comp";
    public string Comment { get; init; } = "";
    public CompCapture Capture { get; init; } = CompCapture.All;
    public ImmutableArray<LayerCompState> Layers { get; init; } = [];
    public bool IsValid => ID != Guid.Empty && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 256
        && Comment is { Length: <= 10000 } && Capture is > 0 and <= CompCapture.All && !Layers.IsDefault
        && Layers.Length <= 10000 && Layers.All(s => s is not null && s.IsValid)
        && Layers.Select(s => s.LayerID).Distinct().Count() == Layers.Length;
}

public static class LayerComps
{
    private static T? Copy<T>(T? value) => value is null ? default : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, ManifestJson.Options), ManifestJson.Options);
    public static LayerComp Capture(CanvasDocument document, string name, CompCapture capture = CompCapture.All, string comment = "") => new()
    {
        Name = name, Capture = capture, Comment = comment,
        Layers = document.Layers.Select(l => new LayerCompState(l.ID, l.IsVisible, l.Transform, l.Opacity, l.FillOpacity,
            l.BlendMode, Copy(l.Effects), Copy(l.Blending))).ToImmutableArray()
    };

    public static bool Apply(CanvasDocument document, LayerComp comp, CompCapture? fields = null, bool respectLocks = true)
    {
        if (!comp.IsValid) return false;
        var capture = fields ?? comp.Capture;
        var changed = false;
        foreach (var saved in comp.Layers)
        {
            var layer = document.Layers.FirstOrDefault(l => l.ID == saved.LayerID);
            if (layer is null || respectLocks && layer.Locks.HasFlag(LayerLocks.All)) continue;
            var before = layer.Clone();
            if (capture.HasFlag(CompCapture.Visibility)) layer.IsVisible = saved.Visible;
            if (capture.HasFlag(CompCapture.Position) && (!respectLocks || !layer.Locks.HasFlag(LayerLocks.Position))) layer.Transform = saved.Transform;
            if (capture.HasFlag(CompCapture.Appearance))
            {
                layer.Opacity = saved.Opacity; layer.FillOpacity = saved.FillOpacity; layer.BlendMode = saved.BlendMode;
                // Equal values retain identity so applying the same comp is not a spurious undo step.
                if (JsonSerializer.Serialize(layer.Effects) != JsonSerializer.Serialize(saved.Effects)) layer.Effects = Copy(saved.Effects);
                if (JsonSerializer.Serialize(layer.Blending) != JsonSerializer.Serialize(saved.Blending)) layer.Blending = Copy(saved.Blending);
            }
            changed |= !layer.SameAs(before);
        }
        return changed;
    }

    public static LayerComp Update(CanvasDocument document, LayerComp comp, CompCapture fields)
    {
        var current = Capture(document, comp.Name, comp.Capture, comp.Comment);
        var old = comp.Layers.ToDictionary(l => l.LayerID);
        return comp with { Layers = current.Layers.Select(now => !old.TryGetValue(now.LayerID, out var previous) ? now : previous with {
            Visible = fields.HasFlag(CompCapture.Visibility) ? now.Visible : previous.Visible,
            Transform = fields.HasFlag(CompCapture.Position) ? now.Transform : previous.Transform,
            Opacity = fields.HasFlag(CompCapture.Appearance) ? now.Opacity : previous.Opacity,
            FillOpacity = fields.HasFlag(CompCapture.Appearance) ? now.FillOpacity : previous.FillOpacity,
            BlendMode = fields.HasFlag(CompCapture.Appearance) ? now.BlendMode : previous.BlendMode,
            Effects = fields.HasFlag(CompCapture.Appearance) ? now.Effects : previous.Effects,
            Blending = fields.HasFlag(CompCapture.Appearance) ? now.Blending : previous.Blending,
        }).ToImmutableArray() };
    }
}
