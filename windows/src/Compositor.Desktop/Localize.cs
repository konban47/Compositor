using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>Translations belong at UI boundaries; project values and shortcut identifiers stay stable.</summary>
internal static class Localize
{
    private static readonly Dictionary<string, string> Chinese = Load();
    private static readonly string Preference = Path.Combine(AppPaths.SettingsDirectory, "language.txt");
    internal static string Language { get; private set; } = ReadLanguage();
    internal static bool IsChinese => Language == "zh-CN";
    internal static FontFamily UiFont => new("avares://Compositor/Assets/Fonts#Noto Sans SC");
    private static readonly Lazy<(Regex Pattern, string Translation)[]> Templates = new(() => Chinese
        .Where(pair => pair.Key.Contains('{'))
        .Select(pair => (new Regex("^" + Regex.Replace(Regex.Escape(pair.Key), @"\\\{\d+(?::[^}]+)?}", "(.*?)") + "$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(25)), pair.Value)).ToArray());

    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(Localize).Assembly.GetManifestResourceStream("Compositor.Desktop.Locales.zh-CN.json")!;
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) result[entry.Key.Replace("_", "")] = entry.Value;
        return result;
    }

    private static string ReadLanguage()
    {
        var requested = Environment.GetEnvironmentVariable("COMPOSITOR_LANGUAGE");
        if (requested is null) try { requested = File.ReadAllText(Preference).Trim(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return (requested ?? CultureInfo.CurrentUICulture.Name).StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en";
    }

    internal static void SaveLanguage(string language)
    {
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        File.WriteAllText(Preference, language);
        // Existing windows keep their language until restart, including active edit dialogs.
    }

    internal static string Text(string? value)
    {
        if (value is null) return "";
        if (!IsChinese) return value;
        var clean = value.Replace("_", "");
        if (Chinese.TryGetValue(clean, out var translation))
        {
            var accelerator = value.IndexOf('_');
            return accelerator >= 0 && accelerator + 1 < value.Length ? translation + "(_" + char.ToUpperInvariant(value[accelerator + 1]) + ")" : translation;
        }
        var suffix = clean.EndsWith('…') ? "…" : clean.EndsWith("...") ? "..." : "";
        if (suffix.Length > 0 && Chinese.TryGetValue(clean[..^suffix.Length], out translation)) return translation + "…";
        foreach (var (pattern, translated) in Templates.Value)
        {
            var match = pattern.Match(clean);
            if (!match.Success) continue;
            return Regex.Replace(translated, @"\{(\d+)(?::[^}]+)?}", token =>
                Text(match.Groups[int.Parse(token.Groups[1].Value, CultureInfo.InvariantCulture) + 1].Value));
        }
        return value;
    }

    internal static string Format(FormattableString value)
    {
        if (!IsChinese) return value.ToString(CultureInfo.CurrentCulture);
        var format = Chinese.GetValueOrDefault(value.Format, value.Format);
        return string.Format(CultureInfo.CurrentCulture, format, value.GetArguments());
    }

    internal static string English(string display)
    {
        var clean = Regex.Replace(display, @"\([A-Z]\)", "").Trim().TrimEnd('…');
        return Chinese.FirstOrDefault(pair => pair.Value == clean).Key ?? clean;
    }

    internal static void Prepare(Window window)
    {
        Avalonia.Controls.Documents.TextElement.SetFontFamily(window, UiFont);
        window.Opened += (_, _) => ApplyChoices(window);
    }

    internal static void ApplyChoices(Control root)
    {
        // A display template preserves selected values, enum values, and keyboard shortcut keys.
        foreach (var combo in root.GetLogicalDescendants().OfType<ComboBox>())
            combo.ItemTemplate ??= new FuncDataTemplate<object>((item, _) =>
                new TextBlock { Text = Text(item?.ToString()), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
    }
}
