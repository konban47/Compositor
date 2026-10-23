using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, StackPanel> _utilityPanels = [];
    private readonly Dictionary<string, Action> _utilityRefresh = [];
    private readonly DispatcherTimer _timelineTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _refreshingUtilities;

    private void OpenUtilityPanel(string name)
    {
        if (!WindowPanelNames.Contains(name)) return;
        if (!_utilityPanels.TryGetValue(name, out var body))
        {
            body = new StackPanel { Spacing = 6, Margin = new Thickness(8) }; _utilityPanels[name] = body;
            var tab = new TabItem { Tag = "utility-panel-" + name, Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
            _inspectorTabs.Items.Add(tab); RegisterPanel(name, _inspectorTabs, tab); BuildUtilityPanel(name, body);
        }
        ShowDockPanel(name); RefreshUtilityPanels();
    }
    private void RefreshUtilityPanels()
    {
        if (_refreshingUtilities) return;
        _refreshingUtilities = true;
        try { foreach (var (name, update) in _utilityRefresh) if (_dockPanels.GetValueOrDefault(name)?.Visible == true) update(); }
        finally { _refreshingUtilities = false; }
    }
    private void BuildUtilityPanel(string name, StackPanel body)
    {
        void Button(string title, Action action) => body.Children.Add(PropertyAction(title, action));
        void Hint(string text) => body.Children.Add(new TextBlock { Text = Localize.Text(text), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Skin.SecondaryBrush });
        switch (name)
        {
            case "Navigator":
            case "Histogram":
            {
                var picture = new Image { Height = 170, Stretch = Stretch.Uniform }; var scope = new ScopesView { Height = 140 };
                body.Children.Add(name == "Navigator" ? picture : scope); WriteableBitmap? previous = null;
                void Update()
                {
                    previous?.Dispose(); previous = null; picture.Source = null;
                    if (_document is not { } doc) { scope.Scope = null; return; }
                    using var pixels = DocumentRenderer.Preview(doc, 300);
                    if (name == "Navigator") { previous = CanvasView.ToImage(pixels); picture.Source = previous; }
                    else scope.Scope = CameraRawScope.OfPremultiplied(pixels);
                }
                _utilityRefresh[name] = Update; Closed += (_, _) => previous?.Dispose();
                if (name == "Navigator")
                {
                    picture.PointerPressed += (_, e) => { if (_document is not { } doc) return; var p = e.GetPosition(picture); var ratio = Math.Min(picture.Bounds.Width / doc.Width, picture.Bounds.Height / doc.Height); var x = (p.X - (picture.Bounds.Width - doc.Width * ratio) / 2) / ratio; var y = (p.Y - (picture.Bounds.Height - doc.Height * ratio) / 2) / ratio; var z = _canvas.Viewport.Zoom; _canvas.RestoreViewport((z, _canvas.Bounds.Width / 2 - x * z, _canvas.Bounds.Height / 2 - y * z)); };
                    body.Children.Add(PropertyPair(PropertyAction("Zoom In", () => _canvas.ZoomBy(1.25)), PropertyAction("Zoom Out", () => _canvas.ZoomBy(.8))));
                    Button("Fit on Screen", () => _canvas.Fit()); Button("Actual Pixels", () => _canvas.ActualSize());
                }
                else { Button("Histogram / Vectorscope", () => scope.Vectorscope = !scope.Vectorscope); Button("Refresh", Update); }
                break;
            }
            case "Adjustments":
                foreach (var kind in Enum.GetValues<AdjustmentKind>()) Button(LayerPlacement.Name(kind), () => _ = NewAdjustment(kind));
                break;
            case "Color": case "Swatches":
            {
                Button("Foreground Color…", () => ChooseColour(true)); Button("Background Color…", () => ChooseColour(false));
                var palette = new WrapPanel(); body.Children.Add(palette);
                foreach (var hex in new[] { "#000000", "#FFFFFF", "#FF4040", "#FF9A30", "#FFE450", "#50C878", "#20B2AA", "#4080FF", "#7050DF", "#E84AAD", "#808080", "#B08050" })
                {
                    var color = SKColor.Parse(hex); var button = new Button { Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(2), Background = new SolidColorBrush(Color.Parse(hex)) };
                    ToolTip.SetTip(button, hex); button.Click += (_, _) => { _options.Brush = _options.Brush with { Red = color.Red / 255d, Green = color.Green / 255d, Blue = color.Blue / 255d }; PushBrush(); }; palette.Children.Add(button);
                }
                break;
            }
            case "Brush Settings": BuildBrushSettingsPanel(body); break;
            case "Clone Source": BuildCloneSourcePanel(body); break;
            case "Gradients":
                foreach (var shape in Enum.GetValues<GradientShape>()) Button(shape.ToString(), () => { SetGradient(shape, null, null); SetTool(Tool.Gradient); });
                Button("Foreground Color…", () => ChooseColour(true)); Button("Background Color…", SetGradientBackground); Button("Reverse", () => SetGradient(null, null, !_options.GradientReversed)); break;
            case "Shapes":
                foreach (var kind in Enum.GetValues<ShapeKind>().Where(k => k is ShapeKind.Rectangle or ShapeKind.Ellipse or ShapeKind.Line)) Button(kind.ToString(), () => { SetTool(Tool.Shape); SetShapeKind(kind); });
                foreach (var shape in ShapeTemplates.Presets) Button(shape.Key, () => { _customShape = shape.Value; SetTool(Tool.CustomShape); });
                Button("Define Shape Preset…", () => _ = DefinePreset("Shape")); Button("Preset Manager…", () => _ = PresetManager()); break;
            case "Brushes": case "Patterns": case "Materials": case "Libraries": case "Tool Presets":
            {
                var list = new ListBox { MinHeight = 90, MaxHeight = 280 }; body.Children.Add(list); EditorPreset[] presets = [];
                void Reload() { presets = EditorPresets.Load(EditorPresets.DefaultPath).Where(p => name is "Libraries" or "Tool Presets" || p.Kind == (name == "Brushes" ? "Brush" : "Pattern")).ToArray(); list.ItemsSource = presets.Select(p => p.Name).ToArray(); }
                Reload(); list.DoubleTapped += (_, _) => { if (list.SelectedIndex is var i && i >= 0 && i < presets.Length) ApplyPreset(presets[i]); };
                Button("Apply Preset", () => { if (list.SelectedIndex is var i && i >= 0 && i < presets.Length) ApplyPreset(presets[i]); });
                Button("Preset Manager…", async () => { await PresetManager(); Reload(); }); Button("Import Presets…", async () => { await MigratePresets(); Reload(); });
                Button("Define Preset…", async () => { await DefinePreset(name is "Patterns" or "Materials" ? "Pattern" : "Brush"); Reload(); });
                if (name is "Patterns" or "Materials") Button("Load Pattern…", () => _ = ChoosePattern());
                if (name == "Materials") Hint("Materials are image patterns for 2D painting.");
                if (name == "Libraries") Hint("This library stores local Compositor presets."); break;
            }
            case "Styles":
                Button("Blending Options…", () => _ = EditLayerStyle()); foreach (var kind in Enum.GetValues<StyleEffectKind>()) Button(LayerStyleDialog.NameFor(kind), () => _ = EditLayerStyle(kind)); Button("Clear Effects", ClearEffects); break;
            case "Paths":
                Button("Pen tool", () => SetTool(Tool.Pen)); Button("Finish Path", () => FinishPen()); Button("Cancel Path", () => CancelPen());
                Button("Path to Selection", () => PathToSelection());
                Button("Delete Work Path", () => { CancelPen(); Edit("Delete Work Path", () => { if (_document?.WorkPath is null) return false; _document.WorkPath = null; return true; }); });
                Button("Create Work Path", CreateTypePath); Button("Define Shape Preset…", () => _ = DefinePreset("Shape")); break;
            case "Notes":
            {
                var list = new ListBox { MinHeight = 80 }; body.Children.Add(list); Guid[] ids = [];
                _utilityRefresh[name] = () => { var notes = _document?.Marks.Where(m => m.Kind == MarkKind.Note).ToArray() ?? []; ids = notes.Select(n => n.ID).ToArray(); list.ItemsSource = notes.Select(n => string.IsNullOrEmpty(n.Name) ? n.Text : n.Name).ToArray(); };
                void Select() { if (list.SelectedIndex is var i && i >= 0 && i < ids.Length) _selectedMark = ids[i]; }
                Button("Note tool", () => SetTool(Tool.Note)); Button("Edit Note…", () => { Select(); _ = EditAnnotation(); }); Button("Delete Note", () => { Select(); DeleteToolAnnotation(); RefreshUtilityPanels(); }); break;
            }
            case "Measurement Log":
                Button("Record Measurements", RecordMeasurements); Button("Measurement Log…", () => _ = MeasurementLog()); Button("Ruler tool", () => SetTool(Tool.Ruler)); break;
            case "Info":
                var info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 }; body.Children.Add(info);
                _utilityRefresh[name] = () => info.Text = _document is { } doc ? $"{_open.Name}\n{doc.Width} × {doc.Height} px\n{Localize.Text("Layers")}: {doc.Layers.Count}\n{Localize.Text("Zoom")}: {_canvas.Viewport.Zoom:P0}\n{Localize.Text("Selected Layer")}: {PropertyLayer?.Name ?? "—"}" : Localize.Text("No document"); break;
            case "Layer Comps":
                Hint("Layer comps use session history snapshots, including pixels and layer properties.");
                var snapshots = new ListBox { MinHeight = 80 }; body.Children.Add(snapshots);
                _utilityRefresh[name] = () => snapshots.ItemsSource = _history.Snapshots.Select(s => s.Name).ToArray();
                Button("Create Snapshot", () => { MakeSnapshot(); RefreshUtilityPanels(); });
                Button("Apply Snapshot", () => { if (snapshots.SelectedIndex is var i && i >= 0 && i < _history.Snapshots.Count) { var saved = _history.Snapshots[i].State.Document; if (saved is not null) Edit("Apply Snapshot", () => { _document!.Adopt(saved); return true; }); } });
                Button("Delete Snapshot", () => { if (snapshots.SelectedIndex is var i && i >= 0 && i < _history.Snapshots.Count) { _history.DeleteSnapshot(_history.Snapshots[i].ID); RefreshUtilityPanels(); } }); break;
            case "Timeline": BuildLayerTimeline(body); break;
            case "Actions": BuildActionsPanel(body); break;
        }
    }
    private void BuildActionsPanel(StackPanel body)
    {
        var names = new[] { "Select All", "Deselect", "Inverse Selection", "Fill with Foreground Color", "Fill with Background Color", "Auto Tone", "Auto Contrast", "Auto Color", "Layer via Copy", "Duplicate Layer", "Toggle Clipping Mask", "Group Layers", "Merge Visible", "Merge Layers", "Proof Colors", "Gamut Warning", "Fit Canvas", "Actual Pixels" };
        var commands = names.Where(n => _verbs.ContainsKey(MenuKey(n))).ToDictionary(n => n, n => (Action)(() => _verbs[MenuKey(n)](ShortcutChord.Unbound)));
        commands["Create Snapshot"] = MakeSnapshot; commands["Clear Effects"] = ClearEffects;
        var sequence = new List<string>();
        var saved = new ComboBox { ItemsSource = EditorPreferences.Current.Actions.Keys.ToArray(), PlaceholderText = Localize.Text("Saved Actions") };
        var choose = new ComboBox { ItemsSource = commands.Keys.Select(Localize.Text).ToArray(), SelectedIndex = 0 }; var list = new ListBox { MinHeight = 80 };
        body.Children.Add(saved); body.Children.Add(choose); body.Children.Add(list);
        void Reload() => list.ItemsSource = sequence.Select(Localize.Text).ToArray();
        saved.SelectionChanged += (_, _) => { if (saved.SelectedItem is string key && EditorPreferences.Current.Actions.TryGetValue(key, out var steps)) { sequence.Clear(); sequence.AddRange(steps.Where(commands.ContainsKey)); Reload(); } };
        body.Children.Add(PropertyPair(PropertyAction("Add Step", () => { if (choose.SelectedIndex >= 0) { sequence.Add(commands.Keys.ElementAt(choose.SelectedIndex)); Reload(); } }), PropertyAction("Delete Step", () => { if (list.SelectedIndex >= 0) { sequence.RemoveAt(list.SelectedIndex); Reload(); } })));
        body.Children.Add(PropertyAction("Play Action", () => { CommitText(); foreach (var command in sequence.ToArray()) commands[command](); }));
        body.Children.Add(PropertyAction("Save Action…", async () => { var name = await TextPrompt.Ask(this, "Save Action", "Name", saved.SelectedItem?.ToString() ?? "Action"); if (string.IsNullOrWhiteSpace(name)) return; EditorPreferences.Current.Actions[name.Trim()] = [.. sequence]; SavePreferenceChanges(); saved.ItemsSource = EditorPreferences.Current.Actions.Keys.ToArray(); saved.SelectedItem = name.Trim(); }));
        body.Children.Add(PropertyAction("Delete Action", () => { if (saved.SelectedItem is string key) { EditorPreferences.Current.Actions.Remove(key); SavePreferenceChanges(); saved.ItemsSource = EditorPreferences.Current.Actions.Keys.ToArray(); } }));
    }

    private void BuildLayerTimeline(StackPanel body) => BuildGifTimeline(body);
}
