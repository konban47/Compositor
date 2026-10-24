using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace Compositor.Core.IO;

public sealed record IccEntry(string Name, string Model, byte[] Bytes);
public static class IccProfiles
{
    private static readonly Lazy<IReadOnlyList<IccEntry>> Installed = new(FindInstalled);
    public static bool Valid(byte[]? bytes) => bytes is { Length: >= 132 and <= 4 * 1024 * 1024 } && Encoding.ASCII.GetString(bytes, 36, 4) == "acsp"
        && BinaryPrimitives.ReadUInt32BigEndian(bytes) >= 132 && BinaryPrimitives.ReadUInt32BigEndian(bytes) <= bytes.Length && Model(bytes) is "RGB" or "CMYK" or "GRAY";
    public static string Model(byte[] bytes) => bytes.Length >= 20 ? Encoding.ASCII.GetString(bytes, 16, 4).Trim() : "";
    public static string Name(byte[] bytes, string fallback)
    {
        if (!Valid(bytes)) return fallback;
        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(128));
        for (var i = 0; i < Math.Min(count, 1024) && 132 + i * 12 + 12 <= bytes.Length; i++)
        {
            var at = 132 + i * 12;
            if (Encoding.ASCII.GetString(bytes, at, 4) != "desc") continue;
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 4)); var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 8));
            if (offset < 0 || length < 12 || (long)offset + length > bytes.Length) continue;
            var type = Encoding.ASCII.GetString(bytes, offset, 4);
            if (type == "desc") { var n = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8)); if (n > 1 && n <= Math.Min(512, length - 12)) { var u = offset + 12 + n; if (u + 8 <= offset + length) { var countU = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(u + 4)); if (countU > 1 && countU < 513 && (long)u + 8 + countU * 2 <= offset + length) return Encoding.BigEndianUnicode.GetString(bytes, u + 8, (countU - 1) * 2); } return Encoding.ASCII.GetString(bytes, offset + 12, n - 1); } }
            if (type == "mluc" && length >= 28) { var n = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 20)); var pos = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 24)); if (n > 0 && n < 1024 && pos >= 0 && (long)pos + n <= length) return Encoding.BigEndianUnicode.GetString(bytes, offset + pos, n); }
        }
        return fallback;
    }
    public static IReadOnlyList<IccEntry> Catalog(ColorManagementSettings settings, string model)
    {
        var entries = Installed.Value.Concat(settings.CustomProfiles.Select(p => new IccEntry(p.Key, Model(p.Value), p.Value)));
        return entries.Where(p => p.Model == model).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).OrderBy(p => p.Name).ToArray();
    }
    public static byte[] Resolve(string name, ColorManagementSettings? settings = null)
    {
        settings ??= ColorDefaults.Settings;
        if (settings.CustomProfiles.TryGetValue(name, out var custom) && Valid(custom)) return custom;
        return Installed.Value.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Bytes
            ?? throw new InvalidDataException("ICC profile not found: " + name);
    }
    private static IReadOnlyList<IccEntry> FindInstalled()
    {
        var entries = new List<IccEntry>();
        foreach (var (name, profile) in new (string, IColorProfile)[] { ("sRGB IEC61966-2.1", ColorProfiles.SRGB), ("Adobe RGB (1998)", ColorProfiles.AdobeRGB1998), ("Apple RGB", ColorProfiles.AppleRGB), ("ColorMatch RGB", ColorProfiles.ColorMatchRGB), ("Coated FOGRA39 (ISO 12647-2:2004)", ColorProfiles.CoatedFOGRA39), ("U.S. Web Coated (SWOP) v2", ColorProfiles.USWebCoatedSWOP) })
            entries.Add(new(name, Model(profile.ToByteArray()), profile.ToByteArray()));
        foreach (var gain in new[] { 10, 15, 20, 25, 30 }) { var name = $"Dot Gain {gain}%"; entries.Add(new(name, "GRAY", Gray(name, 0, gain))); }
        foreach (var gamma in new[] { 1.8, 2.2 }) { var name = "Gray Gamma " + gamma.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); entries.Add(new(name, "GRAY", Gray(name, gamma))); }
        var folders = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool/drivers/color"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "Adobe/Color/Profiles"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), "Adobe/Color/Profiles") };
        foreach (var folder in folders.Distinct())
        {
            if (!Directory.Exists(folder)) continue;
            try { foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(p => Path.GetExtension(p).Equals(".icc", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p).Equals(".icm", StringComparison.OrdinalIgnoreCase)).Take(512))
                { try { if (new FileInfo(path).Length > 4 * 1024 * 1024) continue; var bytes = File.ReadAllBytes(path); if (Valid(bytes)) entries.Add(new(Name(bytes, Path.GetFileNameWithoutExtension(path)), Model(bytes), bytes)); } catch (IOException) { } }
            } catch (UnauthorizedAccessException) { } catch (IOException) { }
        }
        return entries;
    }
    // ICC v2 monochrome input profile: D50 white plus a 16-bit tone reproduction curve.
    public static byte[] Gray(string name, double gamma, double gain = 0, IReadOnlyList<double>? custom = null)
    {
        if (!double.IsFinite(gamma + gain) || gamma < 0 || gamma > 10 || gain < 0 || gain > 50 || custom is not null && (custom.Count != 13 || custom.Any(v => !double.IsFinite(v) || v < 0 || v > 100))) throw new ArgumentException("Invalid gray curve.");
        byte[] Description() { var text = Encoding.ASCII.GetBytes(name.All(c => c < 128) ? name : "Compositor Gray"); var unicode = Encoding.BigEndianUnicode.GetBytes(name + "\0"); var data = new byte[12 + text.Length + 1 + 8 + unicode.Length + 70]; Encoding.ASCII.GetBytes("desc").CopyTo(data, 0); BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)text.Length + 1); text.CopyTo(data, 12); var u = 13 + text.Length; BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(u + 4), (uint)name.Length + 1); unicode.CopyTo(data, u + 8); return data; }
        var white = new byte[20]; Encoding.ASCII.GetBytes("XYZ ").CopyTo(white, 0); foreach (var (offset, v) in new[] { (8, .9642), (12, 1d), (16, .8249) }) BinaryPrimitives.WriteInt32BigEndian(white.AsSpan(offset), (int)Math.Round(v * 65536));
        var curve = new byte[12 + 512 * 2]; Encoding.ASCII.GetBytes("curv").CopyTo(curve, 0); BinaryPrimitives.WriteUInt32BigEndian(curve.AsSpan(8), 512);
        var knots = new double[] { 0, 2, 4, 6, 8, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        var values = custom is null ? null : new[] { 0d }.Concat(custom).Append(100).ToArray();
        double last = 0;
        for (var i = 0; i < 512; i++)
        {
            var x = i / 511d; double value;
            if (values is not null) { var ink = (1 - x) * 100; var j = 0; while (j < knots.Length - 2 && ink > knots[j + 1]) j++; value = 1 - (values[j] + (values[j + 1] - values[j]) * (ink - knots[j]) / (knots[j + 1] - knots[j])) / 100; }
            else value = gamma > 0 ? Math.Pow(x, gamma) : x - gain / 100 * 4 * x * (1 - x);
            value = Math.Clamp(Math.Max(last, value), 0, 1); last = value; BinaryPrimitives.WriteUInt16BigEndian(curve.AsSpan(12 + i * 2), (ushort)Math.Round(value * 65535));
        }
        var copyright = Encoding.ASCII.GetBytes("text\0\0\0\0Compositor generated gray profile\0");
        var tags = new[] { ("desc", Description()), ("wtpt", white), ("kTRC", curve), ("cprt", copyright) }; var size = 132 + tags.Length * 12 + tags.Sum(t => (t.Item2.Length + 3) / 4 * 4); var result = new byte[size];
        void Text(int offset, string s) => Encoding.ASCII.GetBytes(s).CopyTo(result, offset);
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)size); result[8] = 2; result[9] = 0x10; Text(12, "mntr"); Text(16, "GRAY"); Text(20, "XYZ "); Text(36, "acsp"); Text(40, "MSFT");
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(24), 2026); BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(26), 10); BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(28), 24);
        foreach (var (offset, v) in new[] { (68, .9642), (72, 1d), (76, .8249) }) BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset), (int)Math.Round(v * 65536));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(128), (uint)tags.Length); var position = 132 + tags.Length * 12;
        for (var i = 0; i < tags.Length; i++) { var at = 132 + i * 12; Text(at, tags[i].Item1); BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at + 4), (uint)position); BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at + 8), (uint)tags[i].Item2.Length); tags[i].Item2.CopyTo(result, position); position += (tags[i].Item2.Length + 3) / 4 * 4; }
        return result;
    }
}
