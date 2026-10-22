using Compositor.Core.IO;
using Compositor.Core.Model;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private async Task OpenPdf(string path)
    {
        string? password = null; IReadOnlyList<PdfPageSize> sizes;
        while (true)
        {
            try { sizes = await WorkScheduler.Run(_ => PdfImporter.Pages(path, password)); break; }
            catch (PDFtoImage.Exceptions.PdfPasswordProtectedException)
            {
                var unlock = new FormDialog("PDF Password", 420, 220); var input = unlock.Text("Password", ""); input.PasswordChar = '\u2022';
                unlock.Description("This PDF requires a password. It is used only for this import.");
                if (!await unlock.ShowDialog<bool>(this)) return; password = input.Text;
            }
        }
        var form = new FormDialog("Import PDF", 430, 330);
        form.Description(Localize.Text("Each selected page opens as a separate document."));
        var pages = form.Text("Pages (all or 1-3,5)", "all"); var dpi = form.Number("Resolution (ppi)", 72, 1, 2400);
        form.Description($"{sizes.Count} " + Localize.Text("pages"));
        form.Validate = () => { try { PdfImporter.ParsePages(pages.Text ?? "", sizes.Count); return null; } catch (FormatException error) { return error.Message; } };
        if (!await form.ShowDialog<bool>(this)) return;
        foreach (var page in PdfImporter.ParsePages(pages.Text ?? "", sizes.Count))
        {
            var resolution = (int)(dpi.Value ?? 72); var image = await WorkScheduler.Run(token => PdfImporter.Page(path, page, resolution, password, cancellation: token));
            AdoptImported(ImageImporter.NewDocument(image, resolution), "Import PDF"); _open.CustomName = image.Name; RefreshTabs();
        }
    }
}
