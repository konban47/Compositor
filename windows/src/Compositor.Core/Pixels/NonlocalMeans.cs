namespace Compositor.Core.Pixels;

/// <summary>
/// Luminance noise reduction's smoothing, as upstream's AdjustPixels.c has it: non-local means, which keeps edges and
/// texture because only patches that look alike are averaged, and grain, random from patch to patch, averages away.
/// </summary>
internal static class NonlocalMeans
{
    /// <summary>
    /// How much noise <paramref name="luma"/> carries, as its standard deviation (0–1): Immerkær's estimate, from how far
    /// each pixel strays from what its eight neighbors predict, over the pixels that are there.
    /// </summary>
    public static double NoiseSigma(float[] luma, float[] coverage, int width, int height)
    {
        if (width < 3 || height < 3) return 0;
        double sum = 0; long count = 0;
        for (var y = 1; y + 1 < height; y++)
        {
            var up = (y - 1) * width; var row = y * width; var down = (y + 1) * width;
            for (var x = 1; x + 1 < width; x++)
            {
                if (coverage[row + x] < .5f) continue;
                double laplacian = luma[up + x - 1] - 2 * luma[up + x] + luma[up + x + 1] - 2 * luma[row + x - 1] + 4 * luma[row + x] - 2 * luma[row + x + 1]
                    + luma[down + x - 1] - 2 * luma[down + x] + luma[down + x + 1];
                sum += Math.Abs(laplacian);
                count++;
            }
        }
        return count > 0 ? Math.Sqrt(Math.PI / 2) * sum / (6.0 * count) : 0;
    }

    private const int Steps = 1024;
    /// <summary>exp(-d) for d from 0 to 6 in 1024 steps; past 6 a match counts for nothing.</summary>
    private static readonly float[] Falloff = [.. Enumerable.Range(0, Steps + 1).Select(i => (float)Math.Exp(-6.0 * i / Steps))];

    private static int Clamped(int value, int limit) => value < 0 ? 0 : value >= limit ? limit - 1 : value;

    /// <summary>
    /// Each pixel averaged with the others within <paramref name="search"/> pixels whose
    /// <paramref name="patch"/>-wide surroundings look like its own. <paramref name="h"/> is how alike (in brightness, 0–1)
    /// patches must be to count, and <paramref name="sigma"/> the noise, whose share of every distance is taken off first.
    /// Clear pixels lend nothing. Worked one band of rows at a time, an offset at a time, each offset's squared differences
    /// summed over the patch with running sums.
    /// </summary>
    public static void Run(float[] luma, float[] coverage, float[] output, int width, int height, int search, int patch, double h, double sigma)
    {
        var allowance = (float)(2 * sigma * sigma); var toStep = (float)(Steps / (6.0 * h * h));
        var area = (float)((2 * patch + 1) * (2 * patch + 1));
        var bandHeight = Math.Max(16, height / Math.Max(1, Environment.ProcessorCount));
        var bands = (height + bandHeight - 1) / bandHeight;
        Parallel.For(0, bands, band =>
        {
            int first = band * bandHeight, last = Math.Min(height, first + bandHeight), rows = last - first, span = rows + 2 * patch;
            var difference = new float[span * width]; var column = new float[width];
            var total = new float[rows * width]; var weights = new float[rows * width]; var strongest = new float[rows * width];
            for (var dy = -search; dy <= search; dy++)
                for (var dx = -search; dx <= search; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    // Squared differences from this offset, over the band and a patch's height either side.
                    for (var i = 0; i < span; i++)
                    {
                        int y = Clamped(first + i - patch, height), shifted = Clamped(y + dy, height), into = i * width;
                        for (var x = 0; x < width; x++)
                        {
                            var d = luma[y * width + x] - luma[shifted * width + Clamped(x + dx, width)];
                            difference[into + x] = d * d;
                        }
                    }
                    Array.Clear(column);
                    for (var i = 0; i < 2 * patch; i++) for (var x = 0; x < width; x++) column[x] += difference[i * width + x];
                    for (var i = 0; i < rows; i++)
                    {
                        // The column sums down the patch, then across it.
                        var entering = (i + 2 * patch) * width;
                        for (var x = 0; x < width; x++) column[x] += difference[entering + x];
                        int y = first + i, shifted = Clamped(y + dy, height);
                        float across = 0;
                        for (var k = -patch; k < patch; k++) across += column[Clamped(k, width)];
                        for (var x = 0; x < width; x++)
                        {
                            across += column[Clamped(x + patch, width)];
                            var distance = across / area - allowance;
                            across -= column[Clamped(x - patch, width)];
                            var step = distance <= 0 ? 0 : distance * toStep;
                            if (step >= Steps) continue;
                            var neighbor = shifted * width + Clamped(x + dx, width);
                            var weight = Falloff[(int)step] * coverage[neighbor];
                            var index = i * width + x;
                            total[index] += weight * luma[neighbor];
                            weights[index] += weight;
                            if (weight > strongest[index]) strongest[index] = weight;
                        }
                        var leaving = i * width;
                        for (var x = 0; x < width; x++) column[x] -= difference[leaving + x];
                    }
                }
            // The pixel itself counts as much as its best match: as itself it would always count fully, and keep its grain.
            for (var i = 0; i < rows; i++)
                for (var x = 0; x < width; x++)
                {
                    int index = i * width + x, pixel = (first + i) * width + x;
                    var own = strongest[index] > 0 ? strongest[index] : 1;
                    output[pixel] = (total[index] + own * luma[pixel]) / (weights[index] + own);
                }
        });
    }
}
