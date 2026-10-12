using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private (double X, double Y, double W, double H, double Angle, bool FlipX, bool FlipY)? _repeatTransform;
    private void BeginEditTransform(string mode)
    {
        CommitText();
        if (_document is not { } doc || PropertyLayer is not { } layer || !LayerProtection.CanMove(doc, layer.ID)) return;
        if (_placementTab is null)
        {
            _history.Begin("Transform", doc, Selected);
            BeginPlacement(_open, doc.Clone(), Selected, [layer.ID]);
        }
        SetPlacementMode(mode); _canvas.Focus();
    }
    private void RememberTransform()
    {
        if (PropertyLayer is not { } after || _placementBefore?.Layers.FirstOrDefault(l => l.ID == after.ID) is not { } before) return;
        var a = before.Transform; var b = after.Transform;
        if (a.Width <= 0 || a.Height <= 0 || _placeMesh is not null) return;
        _repeatTransform = (b.Point(.5, .5).X - a.Point(.5, .5).X, b.Point(.5, .5).Y - a.Point(.5, .5).Y, b.Width / a.Width, b.Height / a.Height,
            b.Rotation - a.Rotation, a.FlipX != b.FlipX, a.FlipY != b.FlipY);
    }
    private void RepeatTransform()
    {
        if (_repeatTransform is not { } change || _document is not { } doc) return;
        Edit("Transform Again", () =>
        {
            var changed = false;
            foreach (var layer in doc.Layers.Where(l => SelectedLayers.Contains(l.ID) && LayerProtection.CanMove(doc, l.ID)))
            {
                var old = layer.Transform; var w = old.Width * change.W; var h = old.Height * change.H;
                var next = old with { X = old.Point(.5, .5).X + change.X - w / 2, Y = old.Point(.5, .5).Y + change.Y - h / 2,
                    Width = w, Height = h, Rotation = old.Rotation + change.Angle, FlipX = old.FlipX ^ change.FlipX, FlipY = old.FlipY ^ change.FlipY };
                if (!next.IsValid) continue;
                TransformEdits.SetPlacement(layer, next); changed = true;
            }
            return changed;
        });
    }
    private void EditRotate(double angle)
    {
        if (_placementTab is not null) { RotatePlaced(angle); return; }
        if (_document is not { } doc) return;
        Edit("Rotate Object", () =>
        {
            var changed = false;
            foreach (var layer in doc.Layers.Where(l => SelectedLayers.Contains(l.ID) && LayerProtection.CanMove(doc, l.ID)))
            { TransformEdits.SetPlacement(layer, layer.Transform with { Rotation = layer.Transform.Rotation + angle }); changed = true; }
            return changed;
        });
    }
    private async Task CheckSpelling()
    {
        CommitText();
        if (_document is not { } doc || PropertyLayer is not { LiveText: { } text } layer) { Say("Select an editable text layer."); return; }
        var revision = _history.CurrentRevision;
        var language = new FormDialog("Check Spelling", height: 300);
        var tag = language.Text("Dictionary Language", "en-US");
        if (!await language.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document)) return;
        try
        {
            var issues = WindowsSpelling.Check(text.Content, tag.Text ?? "en-US");
            if (issues.Count == 0) { Say("No spelling errors found."); return; }
            var form = new FormDialog("Check Spelling", height: 560);
            form.Description("Edit the replacements to apply. Leave a word unchanged to ignore it.");
            var fields = issues.Select(issue => (Issue: issue, Input: form.Text(issue.Word, issue.Word))).ToArray();
            foreach (var (issue, input) in fields)
                Avalonia.Controls.ToolTip.SetTip(input, string.Join(", ", issue.Suggestions));
            if (!await form.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            var style = CopyFilterSettings(text);
            foreach (var (issue, input) in fields.OrderByDescending(p => p.Issue.Start))
                style.Content = style.Content.Remove(issue.Start, issue.Length).Insert(issue.Start, input.Text ?? "");
            style.ColorRuns = null; style.FontRuns = null;
            Edit("Check Spelling", () => TextEdits.SetStyle(doc, layer.ID, style));
        }
        catch (Exception error) { Say(Localize.Text(error.Message)); }
    }
}
