using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed partial class WebExportDialog
{
    private readonly HashSet<uint> _lockedColors = [], _transparentColors = [], _selected = [];
    private readonly Dictionary<uint, uint> _shifted = [];
    private int _paletteSort;

    private static bool IsWebSafe(SKColor c) => c.Red % 51 == 0 && c.Green % 51 == 0 && c.Blue % 51 == 0;
    private void SelectColors(Func<SKColor, bool> which) { _selected.Clear(); _selected.UnionWith(_displayPalette.Where(p => new SKColor(p).Alpha > 0 && which(new SKColor(p)))); ShowPalette(); }

    private ContextMenu TableMenu()
    {
        var any = _selected.Count > 0; var colors = _displayPalette.Where(p => new SKColor(p).Alpha > 0).ToArray();
        var menu = new ContextMenu();
        menu.Items.Add(Item("New Color", () => _ = NewColor()));
        menu.Items.Add(Item("Delete Color", DeleteColor, any));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Select All Colors", () => SelectColors(_ => true), colors.Length > 0));
        menu.Items.Add(Item("Select All Web Safe Colors", () => SelectColors(IsWebSafe), colors.Any(p => IsWebSafe(new SKColor(p)))));
        menu.Items.Add(Item("Select All Non-Web Safe Colors", () => SelectColors(c => !IsWebSafe(c)), colors.Any(p => !IsWebSafe(new SKColor(p)))));
        menu.Items.Add(Item("Deselect All Colors", () => { _selected.Clear(); ShowPalette(); }, any));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Lock/Unlock Selected Colors", () => ChangePaletteColor(0), any));
        menu.Items.Add(Item("Unlock All Colors", () => { _lockedColors.Clear(); Queue(); }, _lockedColors.Count > 0));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Map/Unmap Selected Colors to Transparent", () => ChangePaletteColor(1), any));
        menu.Items.Add(Item("Unmap All Transparent Colors", () => { _transparentColors.Clear(); Queue(); }, _transparentColors.Count > 0));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Shift/Unshift Selected Colors to Web Palette", () => ChangePaletteColor(2), any));
        menu.Items.Add(Item("Unshift All Colors", UnshiftAll, _shifted.Count > 0));
        menu.Items.Add(new Separator());
        foreach (var (name, sort) in new[] { ("Unsorted", 0), ("Sort by Hue", 1), ("Sort by Luminance", 2), ("Sort by Popularity", 3) })
            menu.Items.Add(Item(name, () => { _paletteSort = sort; SortPalette(); }, true, _paletteSort == sort));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Load Color Table…", () => _ = LoadPalette()));
        menu.Items.Add(Item("Save Color Table…", () => _ = SavePalette(), colors.Length > 0));
        return menu;
    }

    private void ShowPalette()
    {
        _swatches.Children.Clear();
        _tableCount.Text = _displayPalette.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var packed in _displayPalette)
        {
            SKColor c = new(packed); var chosen = _selected.Contains(packed);
            var button = new Button { Width = 18, Height = 18, MinHeight = 0, MinWidth = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(0), Background = new SolidColorBrush(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue)), BorderThickness = new Thickness(chosen ? 2 : 1), BorderBrush = chosen ? Skin.AccentBrush : Skin.BorderControlBrush };
            if (c.Alpha == 0) button.Background = Skin.Checker;
            else if (IsWebSafe(c) || _lockedColors.Contains(packed))
                button.Content = new Avalonia.Controls.Shapes.Rectangle { Width = _lockedColors.Contains(packed) ? 5 : 3, Height = _lockedColors.Contains(packed) ? 5 : 3, Fill = _lockedColors.Contains(packed) ? Brushes.Black : Brushes.White, Stroke = Brushes.Black, StrokeThickness = .5, IsHitTestVisible = false };
            ToolTip.SetTip(button, $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2} · A {c.Alpha}" + (_lockedColors.Contains(packed) ? " · " + Localize.Text("Locked") : ""));
            button.Click += (_, _) =>
            {
                var toggle = _modifiers.HasFlag(KeyModifiers.Control);
                if (!toggle) _selected.Clear();
                if (!_selected.Remove(packed)) _selected.Add(packed);
                ShowPalette();
            };
            button.DoubleTapped += (_, _) => _ = ReplaceColor(packed);
            _swatches.Children.Add(button);
        }
    }
    private KeyModifiers _modifiers;

    private async Task<SKColor?> AskColor(string title, SKColor start)
    {
        var ok = false; (double Red, double Green, double Blue) picked = (start.Red / 255d, start.Green / 255d, start.Blue / 255d);
        var picker = new ColorPickerDialog(title, picked); picker.Applied += color => { ok = true; picked = color; };
        await picker.ShowDialog(this);
        return ok ? new SKColor((byte)Math.Round(picked.Red * 255), (byte)Math.Round(picked.Green * 255), (byte)Math.Round(picked.Blue * 255)) : null;
    }
    private async Task NewColor()
    {
        if (await AskColor("Color Picker (New Color)", _picked) is not { } color || _displayPalette.Length >= 256) return;
        SetPalette([.. _displayPalette, (uint)color.WithAlpha(255)]);
    }
    private async Task ReplaceColor(uint packed)
    {
        if (await AskColor("Color Picker (Edit Color)", new SKColor(packed)) is not { } color) return;
        SetPalette([.. _displayPalette.Select(p => p == packed ? (uint)color.WithAlpha(255) : p)]);
    }
    private void DeleteColor()
    {
        if (_selected.Count == 0) return;
        var left = _displayPalette.Where(p => !_selected.Contains(p)).ToArray(); _selected.Clear();
        SetPalette(left);
    }

    /// <summary>Lock, map to transparent, or shift to the Web palette, on every selected color.</summary>
    private void ChangePaletteColor(int operation)
    {
        var chosen = _selected.Where(p => new SKColor(p).Alpha > 0).ToArray();
        if (chosen.Length == 0) return;
        if (operation == 2) { ShiftColors(chosen); return; }
        foreach (var packed in chosen)
        {
            if (operation == 0) { if (!_lockedColors.Remove(packed)) _lockedColors.Add(packed); }
            else if (!_transparentColors.Remove(packed)) { _lockedColors.Add(packed); _transparentColors.Add(packed); _transparent.IsChecked = true; }
        }
        Queue();
    }
    private void ShiftColors(uint[] chosen)
    {
        byte Snap(byte value) => (byte)Math.Clamp((int)Math.Round(value / 51d) * 51, 0, 255);
        var colors = _displayPalette.ToArray();
        foreach (var packed in chosen)
        {
            var color = new SKColor(packed); var index = Array.IndexOf(colors, packed);
            // A color already shifted goes back to what it was.
            var replacement = _shifted.Remove(packed, out var original) ? original : (uint)new SKColor(Snap(color.Red), Snap(color.Green), Snap(color.Blue));
            if (replacement != packed && !_shifted.ContainsValue(packed)) _shifted[replacement] = packed;
            colors[index] = replacement;
            if (_lockedColors.Remove(packed)) _lockedColors.Add(replacement);
            if (_transparentColors.Remove(packed)) _transparentColors.Add(replacement);
        }
        _selected.Clear(); SetPalette(colors);
    }
    private void UnshiftAll()
    {
        var colors = _displayPalette.Select(p => _shifted.GetValueOrDefault(p, p)).ToArray();
        _shifted.Clear(); SetPalette(colors);
    }

    private void SortPalette()
    {
        if (_results.Length == 0) return;
        IEnumerable<uint> colors = _results[0].Palette;
        if (_paletteSort == 1) colors = colors.OrderBy(p => { new SKColor(p).ToHsv(out var h, out _, out _); return h; });
        if (_paletteSort == 2) colors = colors.OrderBy(p => { var c = new SKColor(p); return c.Red * .2126 + c.Green * .7152 + c.Blue * .0722; });
        if (_paletteSort == 3)
        {
            var counts = _results[0].PaletteUsage;
            colors = colors.OrderByDescending(p => counts.GetValueOrDefault(p));
        }
        _displayPalette = colors.ToArray();
        _selected.IntersectWith(_displayPalette);
        ShowPalette();
    }
}
