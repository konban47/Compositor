using Avalonia.Controls;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed partial class WebExportDialog
{
    private readonly HashSet<uint> _lockedColors = [], _transparentColors = [];
    private int _paletteSort;

    private Button PaletteMenu()
    {
        var button = FormDialog.ActionButton("Color Table Options", () => { });
        var menu = new ContextMenu();
        MenuItem Item(string text, Action action)
        {
            var item = new MenuItem { Header = Localize.Text(text) };
            item.Click += (_, _) => action(); return item;
        }
        menu.ItemsSource = new object[]
        {
            Item("Lock / Unlock Selected Color", () => ChangePaletteColor(0)),
            Item("Unlock All Colors", () => { _lockedColors.Clear(); Queue(); }),
            Item("Map Selected Color to Transparency", () => ChangePaletteColor(1)),
            Item("Clear Transparency Mappings", () => { _transparentColors.Clear(); Queue(); }),
            Item("Convert Selected Color to Web Safe", () => ChangePaletteColor(2)),
            new Separator(),
            Item("Unsorted", () => { _paletteSort = 0; SortPalette(); }),
            Item("Sort by Hue", () => { _paletteSort = 1; SortPalette(); }),
            Item("Sort by Luminance", () => { _paletteSort = 2; SortPalette(); }),
            Item("Sort by Popularity", () => { _paletteSort = 3; SortPalette(); })
        };
        button.ContextMenu = menu; button.Click += (_, _) => menu.Open(button);
        return button;
    }

    private void ChangePaletteColor(int operation)
    {
        if (_selectedColor < 0 || _selectedColor >= _displayPalette.Length) return;
        var packed = _displayPalette[_selectedColor]; var color = new SKColor(packed);
        if (color.Alpha == 0) return;
        if (operation == 0)
        {
            if (!_lockedColors.Remove(packed)) _lockedColors.Add(packed);
        }
        else if (operation == 1)
        {
            _lockedColors.Add(packed); _transparentColors.Add(packed);
            _transparent.IsChecked = true;
        }
        else
        {
            byte Snap(byte value) => (byte)Math.Clamp((int)Math.Round(value / 51d) * 51, 0, 255);
            var replacement = (uint)new SKColor(Snap(color.Red), Snap(color.Green), Snap(color.Blue));
            var colors = _displayPalette.ToArray(); colors[_selectedColor] = replacement;
            var locked = _lockedColors.Remove(packed); var transparent = _transparentColors.Remove(packed);
            SetPalette(colors);
            if (locked) _lockedColors.Add(replacement);
            if (transparent) _transparentColors.Add(replacement);
        }
        Queue();
    }

    private void SortPalette()
    {
        if (_results.Length == 0) return;
        var selected = _selectedColor >= 0 && _selectedColor < _displayPalette.Length
            ? _displayPalette[_selectedColor] : (uint?)null;
        IEnumerable<uint> colors = _results[0].Palette;
        if (_paletteSort == 1) colors = colors.OrderBy(p => { new SKColor(p).ToHsv(out var h, out _, out _); return h; });
        if (_paletteSort == 2) colors = colors.OrderBy(p => { var c = new SKColor(p); return c.Red * .2126 + c.Green * .7152 + c.Blue * .0722; });
        if (_paletteSort == 3)
        {
            var counts = _results[0].PaletteUsage;
            colors = colors.OrderByDescending(p => counts.GetValueOrDefault(p));
        }
        _displayPalette = colors.ToArray();
        _selectedColor = selected.HasValue ? Array.IndexOf(_displayPalette, selected.Value) : -1;
        ShowPalette();
    }
}
