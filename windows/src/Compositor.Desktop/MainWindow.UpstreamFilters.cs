using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private async Task EditFilterLayer(Guid id, LayerAdjustment original)
    {
        if (_document is not { } doc) return; var revision = _history.CurrentRevision;
        var kind = original.Kind switch { AdjustmentKind.Sharpen => FilterKind.Sharpen, AdjustmentKind.Vignette => FilterKind.Vignette, AdjustmentKind.TonalContrast => FilterKind.TonalContrast, AdjustmentKind.LensEffects => FilterKind.LensEffects, AdjustmentKind.ProgressiveBlur => FilterKind.ProgressiveBlur, AdjustmentKind.BloomGlow => FilterKind.BloomGlow, _ => FilterKind.RgbSplit };
        LayerAdjustment Value(FilterSettings settings) => new() { Kind = original.Kind, FilterLayerSettings = FilterLayerSettings.From(settings, doc.Width, doc.Height) };
        StartPreview(doc, id);
        var settings = await FilterDialog.Ask(this, kind, original.FilterLayer.Local(doc.Width, doc.Height), changed => RequestPreview((target, layer) => LayerAdjustmentEdits.Set(target, layer, changed is null ? original : Value(changed))));
        StopPreview();
        if (settings is null || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        Edit(LayerPlacement.Name(original.Kind), () => LayerAdjustmentEdits.Set(doc, id, Value(settings))); Reselect(id);
    }
    private void SplitChannels()
    {
        if (_document is not { } doc || Selected is not { } id || !LayerProtection.CanPaint(doc, id)) return;
        Guid? made = null; Edit("Split Channels", () => (made = ChannelPacking.Split(doc, id)) is not null); if (made is not null) Reselect(made);
    }
    private async Task ExportChannelPack()
    {
        if (_document is not { } doc) return;
        var form = new FormDialog("Channel Pack", 450, 460);
        var sources = new[] { PackedChannelSource.Black, PackedChannelSource.White }.Concat(doc.Layers.Select(l => new PackedChannelSource(l.ID))).ToArray();
        var names = new[] { Localize.Text("Black"), Localize.Text("White") }.Concat(doc.Layers.Select(l => l.Name)).ToArray();
        var fields = new List<ComboBox>();
        for (var channel = 0; channel < 4; channel++) fields.Add(form.Choice(new[] { "Red", "Green", "Blue", "Alpha" }[channel], names, Array.IndexOf(sources, ChannelPacking.Default(doc, channel))));
        var width = form.Number("Width", doc.Width, 1, 30000); var height = form.Number("Height", doc.Height, 1, 30000);
        if (!await form.ShowDialog<bool>(this)) return;
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = Localize.Text("Channel Pack"), SuggestedFileName = "channels.png", DefaultExtension = "png" });
        if (file?.TryGetLocalPath() is not { } path) return;
        var chosen = fields.Select(f => sources[Math.Max(0, f.SelectedIndex)]).ToArray();
        try
        {
            using var lease = new DocumentLease(doc);
            await WorkScheduler.Run(_ => { using var pixels = ChannelPacking.Pack(lease.Document, chosen, (int)(width.Value ?? 1), (int)(height.Value ?? 1)); AtomicFile.Write(path, stream => PngCodec.Encode(pixels, stream)); return true; }, CancellationToken.None);
            Say("Channel pack exported.");
        }
        catch (Exception error) { Say("Could not export channel pack: " + error.Message); }
    }
}
