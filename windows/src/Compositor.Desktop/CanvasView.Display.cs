using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    internal bool ProofColors, GamutWarning;
    private readonly DisplaySceneCache _sceneCache = new();
    private DisplayScene? _displayScene;
    private CanvasDocument? _sceneDocument;
    private bool _sceneDirty = true;
    internal bool AcceleratedScene => _displayScene is not null;
    internal int SceneBuildCount { get; private set; }
    internal static bool LastFrameUsedGpu { get; private set; }
    internal static Action<bool, double>? FrameRendered { get; set; }

    private void ReleaseDisplayScene()
    {
        _displayScene?.Dispose(); _displayScene = null;
        _sceneCache.Dispose(); _sceneDirty = true; _sceneDocument = null;
    }
    private void UpdateDisplayScene(CanvasDocument document)
    {
        if (!_sceneDirty && ReferenceEquals(document, _sceneDocument)) return;
        _displayScene?.Dispose(); _displayScene = null;
        if ((PreviewDocument is null || PreviewResolutionScale == 1) && !ProofColors && !GamutWarning && !Compositor.Core.IO.ColorDefaults.Settings.DesaturateMonitor && EditorPreferences.Current.AcceleratedCanvas && DisplayChannels == ColorChannels.RGB && AlphaDisplay is null)
            _displayScene = _sceneCache.Build(document);
        _sceneDocument = document; _sceneDirty = false; SceneBuildCount++;
    }
    private sealed class SceneDrawOperation(DisplayScene scene, Rect bounds, SKPoint origin, double zoom) : ICustomDrawOperation
    {
        private readonly DisplayScene _scene = scene.Retain();
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() => _scene.Dispose();
        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var canvas = lease.SkCanvas;
            canvas.Save();
            try
            {
                canvas.Scale((float)zoom); canvas.Translate(-origin.X, -origin.Y); _scene.Draw(canvas);
                LastFrameUsedGpu = lease.GrContext is not null;
            }
            finally { canvas.Restore(); }
            FrameRendered?.Invoke(LastFrameUsedGpu, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}
