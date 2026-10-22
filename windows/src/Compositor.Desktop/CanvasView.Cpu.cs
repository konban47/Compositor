using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    private long _cpuVersion, _cpuRequestedVersion = -1, _cpuTileVersion = -1;
    private ViewportTileCache? _cpuTiles;
    private int _cpuRequestedMaximum, _cpuRenderedMaximum;
    private int CpuMaximum => Math.Clamp((int)Math.Ceiling(Math.Max(Bounds.Width, Bounds.Height) * ((VisualRoot as Avalonia.Controls.TopLevel)?.RenderScaling ?? 1)), 256, 2048);
    internal long CpuCachedBytes => _cpuTiles?.RetainedBytes ?? 0;
    internal long CpuTileHits => _cpuTiles?.Hits ?? 0;
    internal long CpuTilesRendered => _cpuTiles?.RenderedTiles ?? 0;
    private SKRectI _cpuRequestedRegion;
    private CanvasDocument? _cpuRequestedDocument;
    private CancellationTokenSource? _cpuCancellation;
    internal bool CpuRenderPending => _cpuCancellation is not null;
    internal static double LastCpuCompositeMilliseconds { get; private set; }
    internal event Action<string>? RenderFailed;

    private async void QueueCpuComposite(CanvasDocument document, SKRectI region)
    {
        var maximum = CpuMaximum;
        if (_cpuRequestedVersion == _cpuVersion && _cpuRequestedRegion == region && _cpuRequestedMaximum == maximum && ReferenceEquals(document, _cpuRequestedDocument)) return;
        if (_cpuTiles is null || _cpuTileVersion != _cpuVersion)
        { _cpuTiles?.Dispose(); _cpuTiles = new ViewportTileCache(); _cpuTileVersion = _cpuVersion; }
        var tiles = _cpuTiles; _cpuRequestedMaximum = maximum;
        _cpuCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _cpuCancellation = cancellation;
        _cpuRequestedVersion = _cpuVersion; var version = _cpuVersion;
        _cpuRequestedRegion = region; _cpuRequestedDocument = document;
        var proof = ProofColors; var gamut = GamutWarning; var channels = DisplayChannels; var opacity = RefinementOpacity ?? .5;
        try
        {
            using var alpha = CopyAlpha();
            using var snapshot = new DocumentLease(document);
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            SKBitmap Render(CancellationToken token) => ViewportRenderer.Render(snapshot.Document, region, maximum, (tile, area) =>
            {
                if (proof || gamut)
                {
                    using var converted = Compositor.Core.IO.CmykColor.Proof(tile, snapshot.Document.Image.CmykProfile ?? Compositor.Core.IO.CmykColor.DefaultProfile, proof, gamut);
                    converted.GetPixelSpan().CopyTo(tile.GetPixelSpan());
                }
                ChannelEdits.View(tile, channels, alpha is null);
                if (alpha is null) return;
                for (var y = 0; y < tile.Height; y++) for (var x = 0; x < tile.Width; x++)
                {
                    var ax = x + area.Left; var ay = y + area.Top;
                    var value = ax < alpha.Width && ay < alpha.Height ? alpha.GetPixel(ax, ay).Red : (byte)0;
                    if (channels == ColorChannels.None) tile.SetPixel(x, y, new SKColor(value, value, value));
                    else
                    {
                        var color = tile.GetPixel(x, y); var mask = (255 - value) / 255.0 * opacity;
                        tile.SetPixel(x, y, new SKColor((byte)(color.Red * (1 - mask) + 255 * mask),
                            (byte)(color.Green * (1 - mask)), (byte)(color.Blue * (1 - mask)), 255));
                    }
                }
            }, token, tiles);
            // A small crop of a large document may touch several complete cache tiles. Keep that work off the UI thread.
            var synchronous = (long)document.Width * document.Height <= 262144 && (long)region.Width * region.Height <= 262144;
            using var rendered = synchronous ? Render(cancellation.Token)
                : await WorkScheduler.Run(Render, cancellation.Token);
            if (cancellation.IsCancellationRequested || version != _cpuVersion || !ReferenceEquals(document, PreviewDocument ?? _document)) return;
            LastCpuCompositeMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var next = ToImage(rendered); _composite?.Dispose(); _composite = next;
            _compositeDocument = document; _compositeRegion = region; _compositeDirty = false; _cpuRenderedMaximum = maximum; CompositeRenderCount++;
            if (!synchronous) base.InvalidateVisual();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!cancellation.IsCancellationRequested) Avalonia.Threading.Dispatcher.UIThread.Post(() => RenderFailed?.Invoke(error.Message)); }
        finally { if (ReferenceEquals(_cpuCancellation, cancellation)) _cpuCancellation = null; }

        SKBitmap? CopyAlpha()
        {
            if (AlphaDisplay is not { } source) return null;
            using var reserved = MemoryBudget.Current.Reserve((long)source.RowBytes * source.Height);
            return source.Copy() ?? throw new OutOfMemoryException("Could not copy the display channel.");
        }
    }

    // Runs on the compositor thread after the CPU image draw, so fallback frames participate in metrics.
    private sealed class CpuFrameOperation(Rect bounds, double buildMilliseconds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            LastFrameUsedGpu = false;
            FrameRendered?.Invoke(false, buildMilliseconds);
        }
    }
}
