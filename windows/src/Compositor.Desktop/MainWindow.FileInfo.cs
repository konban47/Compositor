using Compositor.Core.Format;

namespace Compositor.Desktop;

public partial class MainWindow
{
    private async Task ShowFileInfo()
    {
        CommitText();
        var tab = _open;
        if (tab.Document is not { } document) return;
        var dialog = new FormDialog("File Info", 680, 720);
        var original = document.Metadata;
        var title = dialog.Text("Document Title", original.Title);
        var author = dialog.Text("Author", original.Author);
        var description = dialog.Text("Description", original.Description, true);
        var copyright = dialog.Text("Copyright", original.Copyright);
        var email = dialog.Text("Contact Email", original.Email);
        var website = dialog.Text("Website", original.Website);
        var keywords = dialog.Text("Keywords", original.Keywords);
        var make = dialog.Text("Camera Make", original.CameraMake);
        var model = dialog.Text("Camera Model", original.CameraModel);
        var date = dialog.Text("Date Taken", original.DateTaken);
        DocumentMetadata Value() => new()
        {
            Title = title.Text ?? "", Author = author.Text ?? "", Description = description.Text ?? "",
            Copyright = copyright.Text ?? "", Email = email.Text ?? "", Website = website.Text ?? "",
            Keywords = keywords.Text ?? "", CameraMake = make.Text ?? "", CameraModel = model.Text ?? "",
            DateTaken = date.Text ?? "",
        };
        dialog.Validate = () => Value().IsValid ? null : "Metadata contains invalid characters or exceeds the size limit.";
        if (!await dialog.ShowDialog<bool>(this) || _open != tab || tab.Document != document) return;
        var value = Value();
        if (value == document.Metadata) return;
        Edit("File Info", () => { document.Metadata = value; return true; });
        Refresh();
    }
}
