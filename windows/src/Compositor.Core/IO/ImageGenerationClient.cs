using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SkiaSharp;

namespace Compositor.Core.IO;

public sealed record GenerationRequest(string Endpoint, string Model, string Prompt, string Size = "1024x1024", int Count = 1);
public sealed record GeneratedImage(byte[] Bytes, int Width, int Height);

/// <summary>Explicit user-triggered requests to a configured Images-compatible endpoint.</summary>
public sealed class ImageGenerationClient(HttpClient client)
{
    public static Uri Endpoint(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || !Allowed(uri)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Use an HTTPS API address, or HTTP on localhost.");
        return uri.AbsolutePath.TrimEnd('/').EndsWith("/images/generations", StringComparison.Ordinal)
            ? uri : new Uri(uri.AbsoluteUri.TrimEnd('/') + "/images/generations");
    }
    private static bool Allowed(Uri uri) => uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback;
    public async Task<IReadOnlyList<GeneratedImage>> Generate(GenerationRequest options, string key, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(options.Model) || string.IsNullOrWhiteSpace(options.Prompt) || options.Prompt.Length > 32000
            || options.Count is < 1 or > 4) throw new ArgumentException("Enter a model and a prompt, and request one to four images.");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(options.Endpoint));
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        request.Content = JsonContent.Create(new { model = options.Model.Trim(), prompt = options.Prompt, n = options.Count, size = options.Size });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            // Providers may echo a request. Never propagate its body, headers, URL or credentials to logs/UI.
            throw new HttpRequestException($"Image service returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }
        var bytes = await ReadBounded(response.Content, 128 * 1024 * 1024, cancellation);
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() is < 1 or > 4)
            throw new InvalidDataException("The response must contain one to four images in data.");
        var result = new List<GeneratedImage>();
        foreach (var item in data.EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            byte[] pixels;
            if (item.TryGetProperty("b64_json", out var encoded) && encoded.ValueKind == JsonValueKind.String)
            {
                var text = encoded.GetString()!;
                if (text.Length > 70 * 1024 * 1024) throw new InvalidDataException("Generated image is too large.");
                pixels = Convert.FromBase64String(text);
            }
            else if (item.TryGetProperty("url", out var address) && Uri.TryCreate(address.GetString(), UriKind.Absolute, out var url) && Allowed(url) && url.UserInfo.Length == 0)
            {
                // A result URL never receives the API's Authorization header.
                using var download = new HttpRequestMessage(HttpMethod.Get, url);
                using var downloaded = await client.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, cancellation);
                if (!downloaded.IsSuccessStatusCode) throw new HttpRequestException("Could not download the generated image.");
                pixels = await ReadBounded(downloaded.Content, 50 * 1024 * 1024, cancellation);
            }
            else throw new InvalidDataException("The image has neither b64_json nor a supported URL.");
            using var stream = new MemoryStream(pixels);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 32_000_000)
                throw new InvalidDataException("The generated image is invalid or exceeds 32 megapixels.");
            result.Add(new(pixels, codec.Info.Width, codec.Info.Height));
        }
        return result;
    }
    private static async Task<byte[]> ReadBounded(HttpContent content, int limit, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Image response is too large.");
        await using var stream = await content.ReadAsStreamAsync(cancellation);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellation); if (count == 0) break;
            if (output.Length + count > limit) throw new InvalidDataException("Image response is too large.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
