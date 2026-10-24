using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.IO.PSD;
using Compositor.Core.Model;
using Compositor.Core.Format;
using LayerTransform = Compositor.Core.Model.LayerTransform;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private (Tab Tab, Guid Layer)? _cameraRawAfterImport;
    private void QueueImportedCameraRaw(Tab tab, Guid layer)
    {
        _cameraRawAfterImport = (tab, layer);
        if (!_importingDrop) OpenImportedCameraRaw();
    }
    private void OpenImportedCameraRaw()
    {
        var pending = _cameraRawAfterImport; _cameraRawAfterImport = null;
        if (pending is not { } request || !_tabs.Contains(request.Tab) || request.Tab.Document?.Layers.Any(l => l.ID == request.Layer) != true) return;
        FinishPlacement(true); Bring(request.Tab); Reselect(request.Layer); CameraRawFilter();
    }
    private async Task ImportImageFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Import Folder as Layers…") });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
        var paths = Directory.EnumerateFiles(folder).Where(p => ImageImporter.Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) { Say("No supported images in this folder."); return; }
        await ImportDroppedPaths(paths);
    }

    private async Task SaveTab(Tab tab, bool asNew)
    {
        if (ReferenceEquals(tab, _placementTab)) FinishPlacement(true);
        if (ReferenceEquals(tab, _open)) { FinishPen(); CommitText(); }
        if (tab.Document is null) return;
        if (!asNew && SaveSmartContents(tab)) return;
        if (tab.Saving is { } pending) { await pending; return; }
        var path = tab.Path;
        if (asNew || path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Localize.Text("Save Compositor project folder"), SuggestedFileName = path is null ? "Untitled.comp" : Path.GetFileName(path),
                DefaultExtension = "comp", ShowOverwritePrompt = true,
            });
            if (file?.TryGetLocalPath() is not { } picked) return;
            path = picked.EndsWith(".comp", StringComparison.OrdinalIgnoreCase) ? picked : picked + ".comp";
        }
        if (File.Exists(path)) { Say("Choose a folder name ending in .comp, not an existing file."); return; }
        if (EditorPreferences.Current.ArtboardShrinkOnSave)
        {
            tab.History.Begin("Shrink Artboard Canvas", tab.Document, ReferenceEquals(tab, _open) ? Selected : null);
            FitArtboardCanvas(tab.Document, true); tab.History.End(tab.Document, ReferenceEquals(tab, _open) ? Selected : null);
        }
        tab.Saving = WriteTab(tab, path);
        try { await tab.Saving; }
        finally { tab.Saving = null; }
    }

    private async Task<bool> WriteTab(Tab tab, string path)
    {
        var snapshot = ProjectSnapshot.FromDocument(tab.Document!);
        var revision = tab.History.CurrentRevision;
        try
        {
            Say($"Saving {Path.GetFileName(path)}…");
            await Task.Run(() => ProjectStore.Save(snapshot, path));
            tab.Path = path;
            tab.History.MarkSaved(revision);
            tab.Watch = ProjectWatch.For(path);
            NoteRecent(path);
            if (ReferenceEquals(tab, _open)) { WatchProject(); Refresh(); }
            RefreshTabs();
            Say($"Saved {path}");
            return true;
        }
        catch (Exception error) { Say($"Could not save: {error.Message}"); return false; }
    }

    /// <summary>The file picker, command line and shell integration use the same import path.</summary>
    internal Task OpenPath(string path, bool newTab = true) => OpenPath(path, newTab, null);

    private async Task OpenPath(string path, bool newTab, Tab? importTarget)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (Directory.Exists(path)) { Open(path); return; }
            if (Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            { Open(Path.GetDirectoryName(path)!); return; }
            var extension = Path.GetExtension(path);
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) { await OpenPdf(path); return; }
            if (extension.Equals(".psd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".psb", StringComparison.OrdinalIgnoreCase))
            {
                var imports = await WorkScheduler.Run(token => PsdArtboards.Read(path, token), CancellationToken.None);
                var notes = imports.SelectMany(i => i.Import.Notes).ToArray();
                if (notes.Length > 0 && !await ConfirmDialog.Ask(this, "Photoshop conversion report",
                    string.Join(Environment.NewLine, notes.Select(note => $"{note.Layer}: {Localize.Text(note.What)}")), "Import", "Cancel"))
                { foreach (var item in imports) item.Import.Snapshot().Dispose(); return; }
                foreach (var item in imports) { AdoptImported(item.Import.Snapshot().ToDocument(), "Import Photoshop"); _open.CustomName = item.Name; }
                RefreshTabs(); Say($"Imported {Path.GetFileName(path)}: {imports.Count} document(s).");
                return;
            }
            if (!ImageImporter.LooksImportable(path)) throw new ImportException(ImportError.Unsupported);
            var target = importTarget ?? _open;
            var frame = ReferenceEquals(target, _open) && Selected is { } selectedFrame
                ? target.Document?.Layers.FirstOrDefault(l => l.ID == selectedFrame && l.Container == LayerContainer.Frame && LayerProtection.CanMove(target.Document, l.ID)) : null;
            var fitting = target.Document is { } fit ? new SKSizeI(fit.Width, fit.Height) : (SKSizeI?)null;
            var colorWarning = await Task.Run(() => ColorWorkflow.Warning(path, !newTab, EditorPreferences.Current.ColorSettings));
            if (colorWarning is not null && !await ConfirmDialog.Ask(this, "Color Profile", Localize.Text(colorWarning), "Import", "Cancel")) return;
            var image = await Task.Run(() => ImageImporter.Decode(path, fitting));
            var openCameraRaw = false;
            if (ImageImporter.IsRaw(path))
            {
                var developed = await RawDevelopDialog.Develop(this, image);
                if (developed is null) { image.Dispose(); return; }
                image = developed.Image; openCameraRaw = developed.OpenCameraRaw;
            }
            if (newTab || target.Document is null)
            {
                AdoptImported(ImageImporter.NewDocument(image), "Import Image");
                if (openCameraRaw && Selected is { } id) QueueImportedCameraRaw(_open, id);
                return;
            }
            if (!_tabs.Contains(target)) { image.Dispose(); return; }
            var document = target.Document!;
            var pixels = document.Layers.Sum(layer => (long)(layer.Asset?.Width ?? 0) * (layer.Asset?.Height ?? 0));
            if (pixels + (long)image.Width * image.Height > DocumentLimits.DocumentPixelBudget)
            { image.Dispose(); throw new ImportException(ImportError.TooLarge); }
            var origin = new SKPoint((document.Width - image.Width) / 2f, (document.Height - image.Height) / 2f);
            target.History.Begin("Import Image", document, null);
            var placement = new LayerTransform(origin.X, origin.Y, image.Width, image.Height);
            if (frame is not null && document.Layers.Contains(frame))
            {
                var scale = Math.Max(frame.Transform.Width / image.Width, frame.Transform.Height / image.Height);
                placement = new LayerTransform(frame.Transform.CenterX - image.Width * scale / 2, frame.Transform.CenterY - image.Height * scale / 2,
                    image.Width * scale, image.Height * scale, frame.Transform.Rotation);
            }
            if (frame is null && EditorPreferences.Current.ResizeOnPlace)
            {
                var scale = Math.Min(1, Math.Min((double)document.Width / image.Width, (double)document.Height / image.Height));
                placement = new LayerTransform((document.Width - image.Width * scale) / 2, (document.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
            }
            var placed = new ImageLayer(Guid.NewGuid(), image, placement, image.Name) { ParentID = frame?.ID };
            document.Layers.Add(placed);
            if (EditorPreferences.Current.SmartOnPlace) LayerWorkflow.ConvertToSmartObject(document, [placed.ID]);
            target.History.End(document, document.Layers[^1].ID);
            if (ReferenceEquals(target, _open)) { Reselect(document.Layers[^1].ID); Refresh(); }
            RefreshTabs();
            if (openCameraRaw) QueueImportedCameraRaw(target, placed.ID);
        }
        catch (Exception error) { Say($"Could not open {Path.GetFileName(path)}: {error.Message}"); }
    }

    private void AdoptImported(CanvasDocument document, string name)
    {
        _noteEditor?.Close();
        _open = TabForNew();
        _document = document;
        _projectPath = null;
        _history.Reset();
        _history.MarkUnsaved();
        Show(_open);
        if (document.Layers.Count > 0) Reselect(document.Layers[^1].ID);
        Say($"{name}: {document.Width} × {document.Height}, {document.Layers.Count} layers. Save as .comp to keep editing.");
    }
}
