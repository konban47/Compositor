using System.Text.Json;

namespace Compositor.Core.IO;

public sealed record BatchRecipe(int Version, BatchOptions Options, bool IncludeSubfolders = false)
{
    public static BatchRecipe Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Settings file is too large.");
        var recipe = JsonSerializer.Deserialize<BatchRecipe>(stream);
        if (recipe is null || recipe.Version != 1 || recipe.Options is null)
            throw new InvalidDataException("Invalid batch settings file.");
        BatchProcessing.Validate(recipe.Options);
        return recipe;
    }

    public void Save(string path)
    {
        if (Version != 1 || Options is null) throw new InvalidDataException("Invalid batch settings file.");
        BatchProcessing.Validate(Options);
        AtomicFile.WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
