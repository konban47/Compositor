using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using Compositor.Core.Rendering;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public partial class MainWindow
{
    private MenuItem BuildFileMenu() => new()
    {
        Header = Localize.Text("_File"),
        Items =
        {
            Command("_New Project…", () => _ = NewProject(), "New Project"),
            Command("Open File…", () => _ = OpenImageFile()),
            Command("Open As…", () => _ = OpenImageFile()),
            Command("Open as Smart Object…", () => _ = OpenSmartImage(false)),
            Command("_Open project folder…", OpenProject, "Open Project"),
            _recentMenu,
            new Separator(),
            Command("_Close Tab", () => _ = CloseTab(_open), "Close Tab"),
            Command("Close All", () => _ = CloseDocuments(false)),
            Command("Close Others", () => _ = CloseDocuments(true)),
            Command("_Save", Save, "Save"),
            Command("Save _As…", SaveAs, "Save As"),
            Command("Save a Copy…", () => _ = SaveCopy()),
            Command("Revert", () => _ = RevertSavedProject()),
            new Separator(),
            new MenuItem
            {
                Header = Localize.Text("Export"),
                Items =
                {
                    Command("Quick Export as PNG", ExportPng, "Export PNG"),
                    Command("Export As…", () => _ = ExportAs(), "Export As"),
                    new Separator(),
                    Command("Save for Web…", () => _ = ExportWeb()),
                    new Separator(),
                    Command("Export _JPEG…", () => _ = ExportJpeg(), "Export JPEG"),
                    Command("Channel Pack…", () => _ = ExportChannelPack()),
                    new Separator(),
                    Command("Export Layers to Files…", () => _ = ExportLayers(LayerExportKind.Layers)),
                    Command("Export Layer Comps to PDF…", () => _ = ExportLayers(LayerExportKind.LayerCompsPdf)),
                    Command("Export Layer Comps to Files…", () => _ = ExportLayers(LayerExportKind.LayerComps)),
                    Command("Export Notes…", () => _ = ExportDocumentNotes()),
                    new Separator(),
                    Command("Render Video…", () => _ = RenderTimeline()),
                },
            },
            new Separator(),
            Command("Place Embedded…", () => _ = OpenSmartImage(true)),
            Command("Place Linked…", () => _ = PlaceLinked()),
            Command("_Import image…", () => _ = ImportImage()),
            Command("Import Folder as Layers…", () => _ = ImportImageFolder()),
            Command("Package…", () => _ = PackageDocument()),
            new Separator(),
            new MenuItem
            {
                Header = Localize.Text("Automate"),
                Items =
                {
                    Command("Batch…", () => _ = ShowBatch(false)),
                    Command("PDF Presentation…", () => _ = ShowAutomation(AutomationKind.PdfPresentation)),
                    Command("Create Droplet…", () => _ = ShowBatch(false, true)),
                    new Separator(),
                    Command("Contact Sheet II…", () => _ = ShowAutomation(AutomationKind.ContactSheet)),
                    new Separator(),
                    Command("Photomerge…", () => _ = ShowAutomation(AutomationKind.Photomerge)),
                    Command("Merge to HDR Pro…", () => _ = ShowAutomation(AutomationKind.HdrPro)),
                    Command("Lens Correction…", () => _ = ShowAutomation(AutomationKind.LensCorrection)),
                    Command("Conditional Mode Change…", () => _ = ConditionalModeChange()),
                    Command("Fit Image…", () => _ = FitImage()),
                },
            },
            new MenuItem
            {
                Header = Localize.Text("Scripts"),
                Items =
                {
                    Command("Image Processor…", () => _ = ShowBatch(true)),
                    new Separator(),
                    Command("Delete All Empty Layers", DeleteAllEmptyLayers),
                    new Separator(),
                    Command("Flatten All Masks", FlattenAllMasks),
                    Command("Flatten All Layer Effects", FlattenAllLayerEffects),
                    new Separator(),
                    Command("Script Events Manager…", () => _ = ShowScriptEvents()),
                    new Separator(),
                    Command("Load Files into Stack…", () => _ = ShowAutomation(AutomationKind.LoadLayers)),
                    Command("Image Statistics…", () => _ = ShowAutomation(AutomationKind.Statistics)),
                    Command("Load Multiple DICOM Files…", () => _ = ImportDicomFiles()),
                    new Separator(),
                    Command("Browse…", () => _ = BrowseScript()),
                },
            },
            new MenuItem
            {
                Header = Localize.Text("Import"),
                Items =
                {
                    Command("Variable Data Sets…", () => _ = ImageDataSets(false)),
                    Command("Video Frames to Layers…", () => _ = ImportVideoFrames()),
                    Command("Import Notes…", () => _ = ImportDocumentNotes()),
                    Command("WIA Support…", () => _ = ImportWia()),
                },
            },
            new Separator(),
            Command("File Info…", () => _ = ShowFileInfo()),
            new Separator(),
            Command("Print…", () => _ = PrintDocument()),
            Command("Print One Copy", () => _ = PrintDocument()),
            new Separator(),
            Command("E_xit", Close),
        },
    };

    /// <summary>Prints through Windows: the flattened picture goes to a temporary file, and the system's print dialog does the rest.</summary>
    private async Task PrintDocument()
    {
        if (_document is not { } document || !OperatingSystem.IsWindows()) return;
        try
        {
            using var lease = new DocumentLease(document);
            var path = Path.Combine(Directory.CreateTempSubdirectory("compositor-print-").FullName, (_open?.Name ?? "Print") + ".png");
            await WorkScheduler.Run(token =>
            {
                using var pixels = Compositor.Core.Rendering.DocumentRenderer.Render(lease.Document);
                using var data = pixels.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                AtomicFile.WriteBytes(path, data.ToArray(), token); return true;
            });
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" });
        }
        catch (Exception error) { Say($"Could not print: {error.Message}"); }
    }

    private async Task OpenImageFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localize.Text("Open File…"), AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(Localize.Text("Images"))
                { Patterns = ImageImporter.Extensions.Select(extension => "*" + extension).Concat(["*.pdf", "*.psd", "*.psb"]).ToArray() },
                FilePickerFileTypes.All,
            ],
        });
        foreach (var file in files)
            if (file.TryGetLocalPath() is { } path) await OpenPath(path);
    }
}
