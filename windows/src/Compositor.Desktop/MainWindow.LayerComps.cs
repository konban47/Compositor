using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private Guid? _selectedComp;
    private CanvasDocument? _compDocument;
    private LayerComp? _lastCompState;
    private void BuildLayerCompsPanel(StackPanel body)
    {
        var list = new ListBox { Tag = "layer-comps-list", MinHeight = 145, HorizontalAlignment = HorizontalAlignment.Stretch };
        list.ItemTemplate = new FuncDataTemplate<LayerComp>((comp, _) => new StackPanel { Spacing = 3, Children = {
            new TextBlock { Text = "▤  " + comp?.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = comp?.Comment, IsVisible = !string.IsNullOrWhiteSpace(comp?.Comment), FontSize = 10, Foreground = Skin.SecondaryBrush, TextWrapping = TextWrapping.Wrap }
        } });
        var loading = false;
        list.SelectionChanged += (_, _) => { if (!loading) _selectedComp = (list.SelectedItem as LayerComp)?.ID; };
        LayerComp? SelectedComp() => _document?.LayerComps.FirstOrDefault(c => c.ID == _selectedComp);
        void Apply()
        {
            CommitText(); if (_document is not { } doc || SelectedComp() is not { } comp) return;
            _lastCompState ??= LayerComps.Capture(doc,"Last Document State");
            Edit("Apply Layer Comp", () => LayerComps.Apply(doc,comp));
            var missing = comp.Layers.Count(l => doc.Layers.All(actual => actual.ID != l.LayerID));
            if (missing > 0) Say(Localize.Text("Some recorded layers no longer exist.") + $" ({missing})");
        }
        void Restore() { if (_document is { } doc && _lastCompState is { } saved) Edit("Restore Last Document State", () => LayerComps.Apply(doc,saved)); }
        void Step(int delta)
        {
            if (_document is not { } doc || doc.LayerComps.Count == 0) return;
            var index = doc.LayerComps.FindIndex(c => c.ID == _selectedComp); _selectedComp = doc.LayerComps[(index + delta + doc.LayerComps.Count) % doc.LayerComps.Count].ID;
            RefreshUtilityPanels(); Apply();
        }
        void Update(CompCapture fields)
        {
            CommitText(); if (_document is not { } doc || SelectedComp() is not { } comp) return;
            Edit("Update Layer Comp", () => { doc.LayerComps[doc.LayerComps.IndexOf(comp)] = LayerComps.Update(doc,comp,fields); return true; });
        }
        void Delete() { if (_document is { } doc && SelectedComp() is { } comp) Edit("Delete Layer Comp", () => doc.LayerComps.Remove(comp)); }
        async Task Options(bool create)
        {
            CommitText(); if (_document is not { } doc || create && doc.LayerComps.Count >= 1000) return; var comp = SelectedComp(); if (!create && comp is null) return;
            var form = new FormDialog(create ? "New Layer Comp" : "Layer Comp Options",470,370);
            var name = form.Text("Name",create ? $"{Localize.Text("Layer Comp")} {doc.LayerComps.Count + 1}" : comp!.Name);
            var comment = form.Text("Comment",create ? "" : comp!.Comment); name.MaxLength = 256; comment.MaxLength = 10000;
            var fields = create ? CompCapture.All : comp!.Capture;
            var visibility = form.Check("Visibility",fields.HasFlag(CompCapture.Visibility)); var position = form.Check("Position",fields.HasFlag(CompCapture.Position)); var appearance = form.Check("Appearance",fields.HasFlag(CompCapture.Appearance));
            CompCapture Read() => (visibility.IsChecked == true ? CompCapture.Visibility : 0) | (position.IsChecked == true ? CompCapture.Position : 0) | (appearance.IsChecked == true ? CompCapture.Appearance : 0);
            form.Validate = () => string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 256 || Read() == 0 ? "Enter a name and select at least one property." : null;
            if (!await form.ShowDialog<bool>(this) || doc != _document) return;
            Edit(create ? "New Layer Comp" : "Layer Comp Options", () =>
            {
                if (create) { var made = LayerComps.Capture(doc,name.Text!.Trim(),Read(),comment.Text ?? ""); doc.LayerComps.Add(made); _selectedComp = made.ID; }
                else { var index = doc.LayerComps.FindIndex(c => c.ID == comp!.ID); if (index < 0) return false; doc.LayerComps[index] = comp! with { Name = name.Text!.Trim(), Comment = comment.Text ?? "", Capture = Read() }; }
                return true;
            });
        }
        var menuButton = TypeButton("≡","Layer Comps Menu",() => { }); menuButton.HorizontalAlignment = HorizontalAlignment.Right;
        var menu = new MenuFlyout();
        void RebuildMenu()
        {
            menu.Items.Clear(); var selected = SelectedComp() is not null;
            menu.Items.Add(TypeItem("New Layer Comp…",() => _ = Options(true),enabled:_document is not null && _document.LayerComps.Count < 1000));
            menu.Items.Add(TypeItem("Duplicate Layer Comp",() => { if (_document is { } doc && doc.LayerComps.Count < 1000 && SelectedComp() is { } comp) Edit("Duplicate Layer Comp", () => { var copy = comp with { ID = Guid.NewGuid(), Name = (comp.Name.Length > 240 ? comp.Name[..240] : comp.Name) + " " + Localize.Text("Copy") }; doc.LayerComps.Add(copy); _selectedComp = copy.ID; return true; }); },enabled:selected && _document!.LayerComps.Count < 1000));
            menu.Items.Add(TypeItem("Delete Layer Comp",Delete,enabled:selected)); menu.Items.Add(new Separator());
            foreach (var (title,fields) in new[] { ("Update Layer Comp",CompCapture.All),("Update Visibility",CompCapture.Visibility),("Update Position",CompCapture.Position),("Update Appearance",CompCapture.Appearance) }) menu.Items.Add(TypeItem(title,() => Update(fields),enabled:selected));
            menu.Items.Add(new Separator()); menu.Items.Add(TypeItem("Apply Layer Comp",Apply,enabled:selected)); menu.Items.Add(TypeItem("Next Layer Comp",() => Step(1),enabled:selected)); menu.Items.Add(TypeItem("Previous Layer Comp",() => Step(-1),enabled:selected));
            menu.Items.Add(TypeItem("Restore Last Document State",Restore,enabled:_lastCompState is not null)); menu.Items.Add(TypeItem("Layer Comp Options…",() => _ = Options(false),enabled:selected));
            menu.Items.Add(new Separator()); menu.Items.Add(TypeItem("Close",() => HidePanel(_dockPanels["Layer Comps"])));
            menu.Items.Add(TypeItem("Close Tab Group",() => { if (_dockPanels["Layer Comps"].Group is { } group) foreach (var member in Members(group).ToArray()) HidePanel(member); }));
        }
        menu.Opening += (_, _) => RebuildMenu(); menuButton.Flyout = menu; body.Children.Add(menuButton);
        body.Children.Add(PropertyAction("Last Document State",Restore)); body.Children.Add(list);
        list.DoubleTapped += (_, _) => Apply();
        var footer = new WrapPanel(); foreach (var (glyph,title,action) in new (string,string,Action)[] { ("▶","Apply Layer Comp",Apply),("‹","Previous Layer Comp",() => Step(-1)),("›","Next Layer Comp",() => Step(1)),("◉","Update Visibility",() => Update(CompCapture.Visibility)),("↔","Update Position",() => Update(CompCapture.Position)),("ƒ","Update Appearance",() => Update(CompCapture.Appearance)),("↻","Update Layer Comp",() => Update(CompCapture.All)),("⊞","New Layer Comp…",() => _ = Options(true)),("⌫","Delete Layer Comp",Delete) }) footer.Children.Add(TypeButton(glyph,title,action)); body.Children.Add(footer);
        _utilityRefresh["Layer Comps"] = () =>
        {
            if (_compDocument != _document) { _compDocument = _document; _lastCompState = null; _selectedComp = null; }
            loading = true; list.ItemsSource = _document?.LayerComps.ToArray() ?? []; list.SelectedItem = SelectedComp(); loading = false;
            foreach (var button in footer.Children.OfType<Button>()) button.IsEnabled = Equals(button.Tag,"New Layer Comp…") ? _document is not null && _document.LayerComps.Count < 1000 : SelectedComp() is not null;
        };
    }
}
