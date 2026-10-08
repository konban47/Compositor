using System.Security.Cryptography;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Offline U²-Net saliency inference. The bundled weights are verified before loading.</summary>
public sealed class SubjectSegmentation : IDisposable
{
    public const string ModelHash = "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8";
    public const int Edge = 320;
    private readonly InferenceSession _session;

    public SubjectSegmentation(string? modelPath = null)
    {
        modelPath ??= Path.Combine(AppContext.BaseDirectory, "models", "u2netp.onnx");
        using (var file = File.OpenRead(modelPath))
        {
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(ModelHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The subject model failed its SHA-256 check. Reinstall the Windows package.");
        }
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount, 1, 8),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _session = new InferenceSession(modelPath, options);
    }

    /// <summary>Returns owned Gray8 coverage at the source size, without changing source pixels.</summary>
    public SKBitmap Predict(SKBitmap source, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var small = Bitmaps.Allocate(Bitmaps.ColorInfo(Edge, Edge));
        using (var canvas = new SKCanvas(small))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(source, SKRect.Create(Edge, Edge), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
        var tensor = new DenseTensor<float>([1, 3, Edge, Edge]);
        var pixels = small.GetPixelSpan();
        byte maximum = 1;
        for (var y = 0; y < Edge; y++)
            for (var x = 0; x < Edge; x++)
                for (var c = 0; c < 3; c++) maximum = Math.Max(maximum, pixels[y * small.RowBytes + x * 4 + c]);
        float[] mean = [0.485f, 0.456f, 0.406f], std = [0.229f, 0.224f, 0.225f];
        for (var y = 0; y < Edge; y++)
            for (var x = 0; x < Edge; x++)
                for (var c = 0; c < 3; c++)
                    tensor[0, c, y, x] = (pixels[y * small.RowBytes + x * 4 + c] / (float)maximum - mean[c]) / std[c];
        using var run = new RunOptions();
        using var registration = cancellation.Register(() => run.Terminate = true);
        float[] predicted;
        try
        {
            using var outputs = _session.Run([NamedOnnxValue.CreateFromTensor(_session.InputNames[0], tensor)],
                [_session.OutputNames[0]], run);
            predicted = outputs[0].AsTensor<float>().ToArray();
        }
        catch (OnnxRuntimeException) when (cancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellation);
        }
        if (predicted.Length != Edge * Edge || predicted.Any(v => !float.IsFinite(v)))
            throw new InvalidDataException("The subject model returned an invalid mask.");
        cancellation.ThrowIfCancellationRequested();
        var low = predicted.Min();
        var range = predicted.Max() - low;
        using var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(Edge, Edge));
        var bytes = mask.GetPixelSpan();
        for (var y = 0; y < Edge; y++)
            for (var x = 0; x < Edge; x++)
                bytes[y * mask.RowBytes + x] = range < 1e-6f ? (byte)0
                    : (byte)Math.Clamp((int)MathF.Round((predicted[y * Edge + x] - low) / range * 255), 0, 255);
        var result = Bitmaps.Scale(mask, source.Width, source.Height);
        // Fully transparent source pixels are never foreground. Partial alpha stays in the source;
        // including it here would square it when the mask is applied.
        using var color = source.Copy(SKColorType.Rgba8888);
        var rgba = color.GetPixelSpan();
        var output = result.GetPixelSpan();
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                if (rgba[y * color.RowBytes + x * 4 + 3] == 0) output[y * result.RowBytes + x] = 0;
        return result;
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>Selection and mask edits share the same prediction, with normal document undo.</summary>
public static class SubjectEdits
{
    /// <summary>All foreground, or only its connected component under a click, as a canvas path.</summary>
    public static SKPath Outline(SKBitmap matte, SKPoint? point = null, byte threshold = 128)
    {
        if (!Bitmaps.IsValidMask(matte)) throw new ArgumentException("Expected a Gray8 mask.", nameof(matte));
        var width = matte.Width;
        var height = matte.Height;
        var pixels = matte.GetPixelSpan();
        var mask = new byte[checked(width * height)];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++) mask[y * width + x] = pixels[y * matte.RowBytes + x] >= threshold ? (byte)255 : (byte)0;
        if (point is { } at)
        {
            var x = (int)Math.Floor(at.X);
            var y = (int)Math.Floor(at.Y);
            if (x < 0 || y < 0 || x >= width || y >= height || mask[y * width + x] == 0) return new SKPath();
            var connected = new byte[mask.Length];
            var queue = new Queue<int>();
            queue.Enqueue(y * width + x);
            connected[y * width + x] = 255;
            while (queue.TryDequeue(out var i))
            {
                if (i % width > 0) Visit(i - 1);
                if (i % width + 1 < width) Visit(i + 1);
                if (i >= width) Visit(i - width);
                if (i + width < mask.Length) Visit(i + width);
            }
            void Visit(int i)
            {
                if (mask[i] == 0 || connected[i] != 0) return;
                connected[i] = 255;
                queue.Enqueue(i);
            }
            mask = connected;
        }
        if (WandPixels.WandTrace(mask, width, height, out var points, out _, out var loops, out var count) != 0)
            throw new InvalidOperationException("The subject outline is too detailed. Try a smaller image.");
        using var builder = new SKPathBuilder();
        var offset = 0;
        for (var loop = 0; loop < count; loop++)
        {
            var corners = new SKPoint[loops[loop]];
            for (var i = 0; i < corners.Length; i++) corners[i] = new(points[(offset + i) * 2], points[(offset + i) * 2 + 1]);
            builder.AddPoly(corners, close: true);
            offset += corners.Length;
        }
        return builder.Detach();
    }

    /// <summary>Multiplies a subject matte into the existing mask in layer coordinates, retaining existing coverage and enabling the result.</summary>
    public static bool RemoveBackground(CanvasDocument document, Guid layerID, SKBitmap matte)
    {
        if (!Bitmaps.IsValidMask(matte)) throw new ArgumentException("Expected a Gray8 mask.", nameof(matte));
        var layer = document.Layers.FirstOrDefault(l => l.ID == layerID);
        if (layer is not { IsGroup: false, Asset: { } asset }) return false;
        if (matte.Width != asset.Width || matte.Height != asset.Height) throw new ArgumentException("Mask size must match layer pixels.", nameof(matte));
        var result = Bitmaps.Allocate(Bitmaps.MaskInfo(asset.Width, asset.Height));
        result.Erase(SKColors.White);
        if (layer.Mask is { } previous)
        {
            var from = BrushEdits.PixelToDocument(layer.MaskTransform, previous.Asset.Width, previous.Asset.Height);
            var target = BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height);
            if (!target.TryInvert(out var inverse)) { result.Dispose(); return false; }
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Black);
            canvas.SetMatrix(SKMatrix.Concat(inverse, from));
            canvas.DrawBitmap(previous.Asset.Image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        }
        var output = result.GetPixelSpan();
        var mask = matte.GetPixelSpan();
        for (var y = 0; y < result.Height; y++)
            for (var x = 0; x < result.Width; x++)
                output[y * result.RowBytes + x] = (byte)((output[y * result.RowBytes + x] * mask[y * matte.RowBytes + x] + 127) / 255);
        layer.Mask = LayerMask.AssetFrom(result);
        return true;
    }
}
