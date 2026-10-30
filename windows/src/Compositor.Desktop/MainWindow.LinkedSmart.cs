using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

/// <summary>Linked smart objects: Place Linked, the Smart Objects menu's update, relink and embed, and File › Package.</summary>
public sealed partial class MainWindow
{
    private async Task<string?> PickImage(string title)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localize.Text(title), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Localize.Text("Images")) { Patterns = [.. ImageImporter.Extensions.Select(extension => "*" + extension)] }],
        });
        return picked.Count == 0 ? null : picked[0].TryGetLocalPath();
    }

    /// <summary>The file placed as a smart object, then made to follow the file.</summary>
    private async Task PlaceLinked()
    {
        FinishPlacement(true); FinishPen(); CommitText();
        var target = _open;
        try
        {
            if (await PickImage("Place Linked…") is not { } path || !_tabs.Contains(target)) return;
            await OpenPath(path, false, target, true);
            FinishPlacement(true);
            if (_document is not { } document || Selected is not { } id || document.Layers.FirstOrDefault(l => l.ID == id)?.SmartObject is null) return;
            if (Edit("Place Linked…", () => SmartObjectLinks.Link(document, id, path))) Say($"Linked to {path}");
        }
        catch (Exception error) { Say(error.Message); }
    }

    private void UpdateLinked(bool selectedOnly)
    {
        if (_document is not { } document) return;
        var missing = SmartObjectLinks.Links(document).Where(l => l.State == LinkState.Missing).Select(l => Path.GetFileName(l.Path)).ToArray();
        var count = 0;
        Edit(selectedOnly ? "Update Modified Content" : "Update All Modified Content",
            () => (count = SmartObjectLinks.UpdateModified(document, selectedOnly ? SelectedLayers.ToArray() : null)) > 0);
        Say(missing.Length > 0 ? string.Format(Localize.Text("Linked files not found: {0}"), string.Join(", ", missing))
            : count > 0 ? string.Format(Localize.Text("Updated {0} linked smart objects"), count) : Localize.Text("Linked smart objects are up to date"));
    }

    private async Task RelinkSmartObject()
    {
        if (_document is not { } document || PropertyLayer is not { SmartObject: not null } layer) return;
        try
        {
            if (await PickImage("Relink to File…") is not { } path || !ReferenceEquals(document, _document)) return;
            Edit("Relink to File…", () => SmartObjectLinks.Link(document, layer.ID, path));
        }
        catch (Exception error) { Say(error.Message); }
    }

    private void EmbedLinked()
    {
        if (_document is not { } document) return;
        Edit("Embed Linked", () => SmartObjectLinks.Embed(document, SelectedLayers.ToArray()));
    }

    /// <summary>File › Package: the project saved into a chosen folder with its linked files copied beside it.</summary>
    private async Task PackageDocument()
    {
        FinishPlacement(true); FinishPen(); CommitText();
        if (_document is not { } document) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Package…"), AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder || !ReferenceEquals(document, _document)) return;
        try
        {
            var name = Path.GetFileNameWithoutExtension(_open?.Name ?? "Untitled");
            var project = Path.Combine(folder, (string.IsNullOrWhiteSpace(name) ? "Untitled" : name) + ".comp");
            (IReadOnlyList<string> Copied, IReadOnlyList<string> Missing) result = ([], []);
            Edit("Package…", () => { result = SmartObjectLinks.Package(document, folder); return result.Copied.Count > 0; });
            using var lease = new DocumentLease(document);
            var snapshot = ProjectSnapshot.FromDocument(lease.Document);
            await WorkScheduler.Run(token => { ProjectStore.Save(snapshot, project, cancellation: token); return true; });
            Say(result.Missing.Count > 0 ? string.Format(Localize.Text("Packaged to {0}; linked files not found: {1}"), folder, string.Join(", ", result.Missing.Select(Path.GetFileName)))
                : string.Format(Localize.Text("Packaged to {0} with {1} linked files"), folder, result.Copied.Count));
        }
        catch (Exception error) { Say($"Could not package: {error.Message}"); }
    }
}