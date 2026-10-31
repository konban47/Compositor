using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>A photo's depth: <see cref="Width"/> × <see cref="Height"/> values, rows top-down, 1 nearest and 0 farthest.</summary>
public sealed record DepthMap(float[] Values, int Width, int Height)
{
    /// <summary>
    /// The map with its depths spread out, <paramref name="amount"/> of the way (0–1) to equalized, as Equalize spreads a
    /// photo's tones: each value moves toward its rank among them all, so depths bunched together (a distant city,
    /// everything past a near subject) come apart into steps, while their order stays the same.
    /// </summary>
    public DepthMap Equalized(float amount)
    {
        if (amount <= 0 || Values.Length < 2) return this;
        var order = Enumerable.Range(0, Values.Length).ToArray();
        Array.Sort(order, (a, b) => Values[a].CompareTo(Values[b]));
        var spread = (float[])Values.Clone();
        var last = (float)(Values.Length - 1);
        for (var rank = 0; rank < order.Length; rank++) spread[order[rank]] += (rank / last - Values[order[rank]]) * amount;
        return this with { Values = spread };
    }
}

/// <summary>
/// How far away each part of a photo is, for Volumetric Light's haze. The Mac build downloads its own 2 MB Core ML
/// model, DepthMini, distilled from Depth Anything V2 Small; Core ML doesn't run on Windows, so this build takes Depth
/// Anything V2 Small itself (Apache-2.0), as the ONNX Community publishes it, quantized to 8 bits (27 MB). It isn't in
/// the package: the first time it's needed it's downloaded from a fixed revision and checked against its SHA-256, then
/// kept in the user's local application data. Until then, or without a connection, <see cref="Depth"/> is null and the
/// haze falls back to its own guess.
/// </summary>
public static class DepthModel
{
    public const string Revision = "4472b7362082ad9968fee890ca0f1e5aca36b93d";
    public const string Sha256 = "fcf51f1b230362b28690bb9d1809bf0431f29cad20534e3f589bd7285547f20d";
    public const long Size = 27_258_801;
    /// <summary>Where the file is published. A new model gets a new file name and folder, so an old copy is never
    /// mistaken for it.</summary>
    public static Uri Source { get; set; } =
        new($"https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/{Revision}/onnx/model_quantized.onnx");
    /// <summary>The model's input size; it squashes the whole picture to this (a multiple of its 14-pixel patches).</summary>
    public const int Side = 518;
    /// <summary>The longer side of the depth as it's kept, after it's been pulled onto the photo's edges.</summary>
    public const int RefinedSide = 640;

    /// <summary>The downloaded model, kept between launches. <c>COMPOSITOR_DEPTH_MODEL_DIR</c> moves it, for tests and
    /// for a copy fetched by hand.</summary>
    public static string Folder => Environment.GetEnvironmentVariable("COMPOSITOR_DEPTH_MODEL_DIR") is { Length: > 0 } folder ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Compositor", "Depth Model 1");
    public static string FilePath => Path.Combine(Folder, "depth-anything-v2-small-int8.onnx");

    /// <summary>For tests and the self checks: what the download goes through instead of the network.</summary>
    public static HttpMessageHandler? Handler { get; set; }

    private static readonly Lock Gate = new();
    private static InferenceSession? _session;
    private static Task<bool>? _loading;
    private static readonly ConditionalWeakTable<SKBitmap, DepthMap> Cache = new();

    /// <summary>Whether the model is loaded and <see cref="Depth"/> works.</summary>
    public static bool IsReady { get { lock (Gate) return _session is not null; } }

    /// <summary>Loads the model, downloading it the first time. True once it's ready; false if it couldn't be had (no
    /// connection, a file that doesn't match), to be tried again next time.</summary>
    public static Task<bool> PrepareAsync()
    {
        lock (Gate)
        {
            if (_session is not null) return Task.FromResult(true);
            if (_loading is { } loading) return loading;
            var started = Task.Run(LoadAsync);
            _loading = started;
            started.ContinueWith(_ => { lock (Gate) _loading = null; }, TaskScheduler.Default);
            return started;
        }
    }

    private static async Task<bool> LoadAsync()
    {
        try
        {
            if (!File.Exists(FilePath)) await DownloadAsync();
            return Load(FilePath);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or InvalidDataException
            or UnauthorizedAccessException or TaskCanceledException or OnnxRuntimeException)
        {
            return false;
        }
    }

    /// <summary>The file, checked as it arrives, then moved into place.</summary>
    private static async Task DownloadAsync()
    {
        Directory.CreateDirectory(Folder);
        var partial = FilePath + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var client = Handler is { } handler ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);
            using var response = await client.GetAsync(Source, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var body = await response.Content.ReadAsStreamAsync())
            await using (var file = File.Create(partial))
                await body.CopyToAsync(file);
            if (!Matches(partial)) throw new InvalidDataException("The depth model failed its SHA-256 check.");
            File.Move(partial, FilePath, overwrite: true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    private static bool Matches(string path)
    {
        using var file = File.OpenRead(path);
        return file.Length == Size && Convert.ToHexString(SHA256.HashData(file)).Equals(Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Loads the model at <paramref name="path"/> once it checks out. A copy that doesn't is removed, so the
    /// next try downloads afresh.</summary>
    public static bool Load(string path)
    {
        if (!Matches(path))
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return false;
        }
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableCpuMemArena = MemoryBudget.Current.Limit > (1024L << 20),
            EnableMemoryPattern = MemoryBudget.Current.Limit > (1024L << 20),
        };
        var session = new InferenceSession(path, options);
        lock (Gate) { _session?.Dispose(); _session = session; }
        return true;
    }

    /// <summary>Drops the loaded model; for tests.</summary>
    public static void Unload() { lock (Gate) { _session?.Dispose(); _session = null; } }

    /// <summary>
    /// <paramref name="image"/>'s depth, 1 nearest and 0 farthest: the model's map, which is soft and a little off the
    /// photo's edges, enlarged to <see cref="RefinedSide"/> and pulled onto those edges with a guided filter (as Remove
    /// Background pulls its mask), so its near and far meet where the photo's own outlines are. Null until the model is
    /// ready. Worked out once an image.
    /// </summary>
    public static DepthMap? Depth(SKBitmap image)
    {
        if (Cache.TryGetValue(image, out var known)) return known;
        InferenceSession? session;
        lock (Gate) session = _session;
        if (session is null) return null;
        var rgba = LightPlanes.Rgba(image, Side, Side);
        var tensor = new DenseTensor<float>([1, 3, Side, Side]);
        float[] mean = [.485f, .456f, .406f], std = [.229f, .224f, .225f];
        for (var y = 0; y < Side; y++)
            for (var x = 0; x < Side; x++)
            {
                var i = (y * Side + x) * 4;
                for (var c = 0; c < 3; c++) tensor[0, c, y, x] = (Math.Clamp(rgba[i + c], 0, 1) * rgba[i + 3] - mean[c]) / std[c];
            }
        float[] predicted;
        lock (session)
        {
            using var outputs = session.Run([NamedOnnxValue.CreateFromTensor(session.InputNames[0], tensor)], [session.OutputNames[0]]);
            predicted = outputs[0].AsTensor<float>().ToArray();
        }
        if (predicted.Length != Side * Side || predicted.Any(v => !float.IsFinite(v))) return null;
        float low = predicted.Min(), high = predicted.Max();
        if (!(high > low)) return null;
        // Enlarged to the photo's shape, then onto its edges, its brightness the guide.
        var (width, height, _) = LightPlanes.Grid(image.Width, image.Height, RefinedSide);
        width = Math.Max(8, width); height = Math.Max(8, height);
        var enlarged = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            var fy = Math.Clamp((y + .5f) * Side / height - .5f, 0, Side - 1);
            var y0 = (int)fy; var y1 = Math.Min(Side - 1, y0 + 1); var ty = fy - y0;
            for (var x = 0; x < width; x++)
            {
                var fx = Math.Clamp((x + .5f) * Side / width - .5f, 0, Side - 1);
                var x0 = (int)fx; var x1 = Math.Min(Side - 1, x0 + 1); var tx = fx - x0;
                var top = predicted[y0 * Side + x0] * (1 - tx) + predicted[y0 * Side + x1] * tx;
                var bottom = predicted[y1 * Side + x0] * (1 - tx) + predicted[y1 * Side + x1] * tx;
                enlarged[y * width + x] = (top * (1 - ty) + bottom * ty - low) / (high - low);
            }
        }
        var guideRgba = LightPlanes.Rgba(image, width, height);
        var guide = new float[width * height];
        for (var i = 0; i < guide.Length; i++)
            guide[i] = .2126f * guideRgba[i * 4] + .7152f * guideRgba[i * 4 + 1] + .0722f * guideRgba[i * 4 + 2];
        var refined = LightPlanes.Guided(enlarged, guide, width, height, Math.Max(2, RefinedSide / 48));
        var depth = new DepthMap(refined, width, height);
        Cache.AddOrUpdate(image, depth);
        return depth;
    }

    /// <summary><paramref name="depth"/> read between its values at a grid of <paramref name="width"/> × <paramref name="height"/>.</summary>
    internal static float[] Resample(DepthMap depth, int width, int height)
    {
        var plane = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                plane[y * width + x] = LightPlanes.Sample(depth.Values, depth.Width, depth.Height,
                    (x + .5f) * depth.Width / width, (y + .5f) * depth.Height / height);
        return plane;
    }
}
