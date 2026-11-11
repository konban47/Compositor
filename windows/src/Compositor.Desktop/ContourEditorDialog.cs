using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>
/// Photoshop's Contour Editor: a Preset list, the Mapping grid with the curve's points (click to add, drag to move, drag
/// off the grid to remove), Input and Output of the point being dragged, and OK, Cancel, Load…, Save… and New….
/// </summary>
internal sealed class ContourEditorDialog : DialogWindow
{
    private double[]? _result;
    internal MappingView Mapping { get; }
    private ContourEditorDialog(double[] start)
    {
        Title = Localize.Text("Contour Editor"); Width = 610; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var input = new TextBlock { Width = 60, Tag = "contour-input" }; var output = new TextBlock { Width = 60, Tag = "contour-output" };
        Mapping = new MappingView(start, (i, o) => { input.Text = i is null ? "" : $"{i:0}"; output.Text = o is null ? "" : $"{o:0}"; }) { Width = 380, Height = 380 };
        var presets = ContourLibrary.Load().ToList();
        var preset = new ComboBox { Width = 320, MinHeight = DialogKit.FieldHeight, Tag = "contour-preset", ItemsSource = presets.Select(p => Localize.Text(p.Name)).Prepend(Localize.Text("Custom")).ToArray() };
        var match = presets.FindIndex(p => p.Contour is { } c && c.Length == start.Length && c.Zip(start).All(z => Math.Abs(z.First - z.Second) < 1e-6));
        preset.SelectedIndex = match + 1; var choosing = false;
        preset.SelectionChanged += (_, _) => { if (preset.SelectedIndex > 0 && !choosing) { choosing = true; Mapping.Load(presets[preset.SelectedIndex - 1].Contour!); choosing = false; } };
        Mapping.Edited += () => { if (!choosing) { choosing = true; preset.SelectedIndex = 0; choosing = false; } };
        var readouts = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 60, RowSpacing = 18, Margin = new Thickness(20, 12, 0, 4) };
        void Put(Control c, int r, int col) { Grid.SetRow(c, r); Grid.SetColumn(c, col); readouts.Children.Add(c); }
        Put(new TextBlock { Text = Localize.Text("Input") + ":" }, 0, 0); Put(new StackPanel { Orientation = Orientation.Horizontal, Children = { input, new TextBlock { Text = "%" } } }, 0, 1);
        Put(new TextBlock { Text = Localize.Text("Output") + ":" }, 1, 0); Put(new StackPanel { Orientation = Orientation.Horizontal, Children = { output, new TextBlock { Text = "%" } } }, 1, 1);
        var body = new StackPanel { Spacing = 10, Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Localize.Text("Preset") + ":", VerticalAlignment = VerticalAlignment.Center, Width = 52, TextAlignment = TextAlignment.Right }, preset } },
            DialogKit.Group("Mapping", new StackPanel { Children = { Mapping, readouts } }),
        } };
        var ok = FormDialog.ActionButton("OK", () => { _result = Mapping.Samples(); Close(true); }); ok.IsDefault = true;
        var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        var load = FormDialog.ActionButton("Load…", () => _ = Load()); this.Mnemonic(load, 'L');
        var save = FormDialog.ActionButton("Save…", () => _ = Save()); this.Mnemonic(save, 'S');
        var fresh = FormDialog.ActionButton("New…", () => _ = New());
        var buttons = DialogKit.ActionColumn(ok, cancel, new Border { Height = 40 }, load, save, new Border { Height = 40 }, fresh); buttons.Width = 130;
        var root = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(18, 14) };
        root.Children.Add(body); Grid.SetColumn(buttons, 1); root.Children.Add(buttons); Content = root;
    }
    internal static async Task<double[]?> Ask(Window owner, double[] start)
    {
        var dialog = new ContourEditorDialog(start);
        return await dialog.ShowDialog<bool>(owner) ? dialog._result : null;
    }
    private async Task Load()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Contour"), FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try { if (new FileInfo(path).Length > 1_000_000) return; var samples = JsonSerializer.Deserialize<double[]>(await File.ReadAllTextAsync(path)); if (ContourLibrary.Valid(samples)) Mapping.Load(samples!); }
        catch (Exception error) when (error is IOException or JsonException) { }
    }
    private async Task Save()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save Contour"), SuggestedFileName = "contour.json", DefaultExtension = "json" });
        if (file?.TryGetLocalPath() is { } path) await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Mapping.Samples()));
    }
    private async Task New()
    {
        var name = await TextPrompt.Ask(this, "Contour Name", "Name", Localize.Text("Contour") + " " + (ContourLibrary.Load().Count + 1));
        if (string.IsNullOrWhiteSpace(name)) return;
        var all = EditorPresets.Load(ContourLibrary.DefaultPath).ToList(); if (!File.Exists(ContourLibrary.DefaultPath)) all.AddRange(ContourLibrary.Defaults());
        all.Add(new EditorPreset(Guid.NewGuid(), name.Trim(), "Contour", Contour: Mapping.Samples())); EditorPresets.Save(ContourLibrary.DefaultPath, all);
    }

    /// <summary>The Mapping grid: the curve through its points, the points as small squares.</summary>
    internal sealed class MappingView : Control
    {
        private readonly List<Point> _points = [];
        private readonly Action<double?, double?> _readout;
        private int _dragging = -1;
        internal event Action? Edited;
        internal MappingView(double[] samples, Action<double?, double?> readout) { _readout = readout; Load(samples); Cursor = new Cursor(StandardCursorType.Cross); }
        internal IReadOnlyList<Point> Points => _points;
        /// <summary>The curve as the points of its corners, found by dropping samples that lie on the line between their neighbours.</summary>
        internal void Load(double[] samples)
        {
            _points.Clear();
            var all = samples.Select((v, i) => new Point(i / (double)(samples.Length - 1), v)).ToList();
            void Reduce(int a, int b)
            {
                var worst = -1; var distance = 0.012;
                for (var i = a + 1; i < b; i++)
                {
                    var t = (all[i].X - all[a].X) / (all[b].X - all[a].X); var y = all[a].Y + (all[b].Y - all[a].Y) * t;
                    if (Math.Abs(all[i].Y - y) > distance) { distance = Math.Abs(all[i].Y - y); worst = i; }
                }
                if (worst < 0) return; Reduce(a, worst); _points.Add(all[worst]); Reduce(worst, b);
            }
            _points.Add(all[0]); Reduce(0, all.Count - 1); _points.Add(all[^1]);
            _points.Sort((p, q) => p.X.CompareTo(q.X)); InvalidateVisual();
        }
        /// <summary>The curve sampled at 65 points, as the contour library and the effects keep it.</summary>
        internal double[] Samples() => Enumerable.Range(0, 65).Select(i => Evaluate(i / 64d)).ToArray();
        private double Evaluate(double x)
        {
            if (x <= _points[0].X) return _points[0].Y; if (x >= _points[^1].X) return _points[^1].Y;
            var right = _points.FindIndex(p => p.X >= x); var a = _points[right - 1]; var b = _points[right];
            return Math.Clamp(a.Y + (b.Y - a.Y) * ((x - a.X) / Math.Max(1e-9, b.X - a.X)), 0, 1);
        }
        private Point ToScreen(Point p) => new(p.X * Bounds.Width, (1 - p.Y) * Bounds.Height);
        private Point ToCurve(Point s) => new(Math.Clamp(s.X / Bounds.Width, 0, 1), Math.Clamp(1 - s.Y / Bounds.Height, 0, 1));
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Skin.SurfaceDarkBrush, new Pen(Skin.BorderControlBrush, 1), new Rect(Bounds.Size));
            var grid = new Pen(Skin.CurveGrid, 1);
            for (var i = 1; i < 10; i++) { var x = Bounds.Width * i / 10; var y = Bounds.Height * i / 10; context.DrawLine(grid, new Point(x, 0), new Point(x, Bounds.Height)); context.DrawLine(grid, new Point(0, y), new Point(Bounds.Width, y)); }
            var line = new StreamGeometry();
            using (var g = line.Open()) { g.BeginFigure(ToScreen(new Point(0, Evaluate(0))), false); for (var i = 1; i <= 128; i++) g.LineTo(ToScreen(new Point(i / 128d, Evaluate(i / 128d)))); g.EndFigure(false); }
            context.DrawGeometry(null, new Pen(Brushes.White, 2), line);
            foreach (var p in _points) { var s = ToScreen(p); context.DrawRectangle(_points.IndexOf(p) == _dragging ? Brushes.White : Skin.SurfaceDarkBrush, new Pen(Brushes.White, 1.2), new Rect(s.X - 4, s.Y - 4, 8, 8)); }
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var at = e.GetPosition(this);
            _dragging = _points.FindIndex(p => Math.Abs(ToScreen(p).X - at.X) < 7 && Math.Abs(ToScreen(p).Y - at.Y) < 7);
            if (_dragging < 0 && _points.Count < 16)
            {
                var point = ToCurve(at); _points.Add(point); _points.Sort((p, q) => p.X.CompareTo(q.X)); _dragging = _points.IndexOf(point); Edited?.Invoke();
            }
            e.Pointer.Capture(this); Move(at); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e) { if (_dragging >= 0 && e.Pointer.Captured == this) Move(e.GetPosition(this)); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            var at = e.GetPosition(this);
            // A point dragged off the grid is taken away, as long as two are left.
            if (_dragging > 0 && _dragging < _points.Count - 1 && (at.X < -12 || at.Y < -12 || at.X > Bounds.Width + 12 || at.Y > Bounds.Height + 12)) { _points.RemoveAt(_dragging); Edited?.Invoke(); }
            _dragging = -1; e.Pointer.Capture(null); _readout(null, null); InvalidateVisual();
        }
        private void Move(Point at)
        {
            if (_dragging < 0) return; var point = ToCurve(at);
            // The end points keep their places along the input; the others stay between their neighbours.
            var minX = _dragging == 0 ? 0 : _points[_dragging - 1].X + .01; var maxX = _dragging == _points.Count - 1 ? 1 : _points[_dragging + 1].X - .01;
            if (_dragging == 0) minX = maxX = 0; if (_dragging == _points.Count - 1) minX = maxX = 1;
            _points[_dragging] = new Point(Math.Clamp(point.X, minX, maxX), point.Y);
            _readout(_points[_dragging].X * 100, _points[_dragging].Y * 100); Edited?.Invoke(); InvalidateVisual();
        }
        /// <summary>For the checks: a click or drag at a point given in 0–1 curve units.</summary>
        internal void DragForCheck(Point from, Point to)
        {
            var start = ToScreen(from); _dragging = _points.FindIndex(p => Math.Abs(ToScreen(p).X - start.X) < 7 && Math.Abs(ToScreen(p).Y - start.Y) < 7);
            if (_dragging < 0) { _points.Add(from); _points.Sort((p, q) => p.X.CompareTo(q.X)); _dragging = _points.IndexOf(from); }
            Move(ToScreen(to)); _dragging = -1; Edited?.Invoke();
        }
    }
}
