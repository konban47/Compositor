using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using ImageMagick;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private MenuItem CmykMenu()
    {
        var menu = new MenuItem { Header = "CMYK" };
        var proof = new MenuItem { Header = Localize.Text("Proof Colors"), ToggleType = MenuItemToggleType.CheckBox };
        var gamut = new MenuItem { Header = Localize.Text("Gamut Warning"), ToggleType = MenuItemToggleType.CheckBox };
        proof.Click += (_, _) => { _canvas.ProofColors = !_canvas.ProofColors; _canvas.InvalidateVisual(); };
        gamut.Click += (_, _) => { _canvas.GamutWarning = !_canvas.GamutWarning; _canvas.InvalidateVisual(); };
        menu.Items.Add(proof); menu.Items.Add(gamut); menu.Items.Add(new Separator());
        foreach (var (name, profile) in new[] { ("US Web Coated (SWOP)", ColorProfiles.USWebCoatedSWOP), ("Coated FOGRA39", ColorProfiles.CoatedFOGRA39) })
            menu.Items.Add(Command(name, () => SelectCmykProfile(profile.ToByteArray())));
        menu.Items.Add(Command("Load CMYK Profile…", () => _ = LoadCmykProfile()));
        menu.SubmenuOpened += (_, _) => { proof.IsChecked = _canvas.ProofColors; gamut.IsChecked = _canvas.GamutWarning; };
        return menu;
    }
    private void SelectCmykProfile(byte[] profile)
    {
        if (_document is not { } doc) return;
        Edit("CMYK Profile", () => { doc.Image = doc.Image with { CmykProfile = profile }; return true; }); _canvas.InvalidateVisual();
    }
    private async Task LoadCmykProfile()
    {
        var doc = _document;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load CMYK Profile…"), FileTypeFilter = [new FilePickerFileType("ICC") { Patterns = ["*.icc", "*.icm"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path || !ReferenceEquals(doc, _document)) return;
        try { if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("ICC profile is too large."); var profile = await File.ReadAllBytesAsync(path); if (!CmykColor.IsProfile(profile)) throw new InvalidDataException("Choose a CMYK ICC profile."); SelectCmykProfile(profile); }
        catch (Exception error) { Say(error.Message); }
    }
}
