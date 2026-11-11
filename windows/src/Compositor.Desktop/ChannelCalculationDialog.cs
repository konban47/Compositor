using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

/// <summary>What Apply Image or Calculations asks for.</summary>
internal sealed record ChannelCalculationResult(ChannelSource First, ChannelSource? Second, CalculationSettings Settings, ChannelSource? Mask, int Result);

/// <summary>
/// Image ▸ Apply Image and Image ▸ Calculations, laid out as Photoshop's: Source (and Source 2), Layer and Channel with
/// Invert; the target or the second source; Blending with Opacity and its slider, Scale and Offset for Add and Subtract;
/// Preserve Transparency and Mask…, which opens the mask's own Image, Layer and Channel; and Calculations' Result.
/// </summary>
internal sealed class ChannelCalculationDialog : DialogWindow
{
    /// <summary>Photoshop's blending list, in its groups.</summary>
    internal static readonly ChannelBlend?[] Blends =
    [
        ChannelBlend.Normal, ChannelBlend.Darken, ChannelBlend.Multiply, ChannelBlend.ColorBurn, ChannelBlend.LinearBurn, ChannelBlend.DarkerColor, null,
        ChannelBlend.Lighten, ChannelBlend.Screen, ChannelBlend.ColorDodge, ChannelBlend.LinearDodge, ChannelBlend.LighterColor, null,
        ChannelBlend.Overlay, ChannelBlend.SoftLight, ChannelBlend.HardLight, ChannelBlend.VividLight, ChannelBlend.LinearLight, ChannelBlend.PinLight, ChannelBlend.HardMix, null,
        ChannelBlend.Add, ChannelBlend.Subtract, null, ChannelBlend.Difference, ChannelBlend.Exclusion, ChannelBlend.Divide,
    ];
    internal static string BlendName(ChannelBlend blend) => blend switch
    {
        ChannelBlend.ColorBurn => "Color Burn", ChannelBlend.LinearBurn => "Linear Burn", ChannelBlend.DarkerColor => "Darker Color", ChannelBlend.ColorDodge => "Color Dodge",
        ChannelBlend.LinearDodge => "Linear Dodge (Add)", ChannelBlend.LighterColor => "Lighter Color", ChannelBlend.SoftLight => "Soft Light", ChannelBlend.HardLight => "Hard Light",
        ChannelBlend.VividLight => "Vivid Light", ChannelBlend.LinearLight => "Linear Light", ChannelBlend.PinLight => "Pin Light", ChannelBlend.HardMix => "Hard Mix", _ => blend.ToString(),
    };

    private ChannelCalculationResult? _result;
    internal ComboBox Blending { get; }

    /// <summary>One source's Image, Layer and Channel rows with Invert, read back as a <see cref="ChannelSource"/>.</summary>
    private sealed class SourceRows
    {
        internal readonly ComboBox Document = new() { Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        internal readonly ComboBox Layer = new() { Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        internal readonly ComboBox Channel = new() { Height = 30, Width = 240 };
        internal readonly CheckBox Invert = new();
        private readonly IReadOnlyList<(string Name, CanvasDocument Document)> _documents;
        private List<Guid?> _layers = [], _alphas = [];
        private readonly bool _gray;
        internal SourceRows(IReadOnlyList<(string Name, CanvasDocument Document)> documents, int selected, char invertKey, bool gray, string tag)
        {
            _documents = documents; _gray = gray;
            Document.ItemsSource = documents.Select(d => d.Name).ToArray(); Document.SelectedIndex = Math.Max(0, selected); Document.Tag = tag + " image";
            Layer.Tag = tag + " layer"; Channel.Tag = tag + " channel"; Invert.Tag = tag + " invert";
            Invert.Content = new TextBlock { Text = Localize.Text("Invert") + $"({invertKey})" };
            Document.SelectionChanged += (_, _) => Fill(); Fill();
        }
        private CanvasDocument Doc => _documents[Math.Max(0, Document.SelectedIndex)].Document;
        private void Fill()
        {
            var doc = Doc;
            // Merged when there are several layers, then each layer top to bottom, as Photoshop lists them.
            _layers = (doc.Layers.Count > 1 ? new List<Guid?> { null } : []).Concat(doc.Layers.AsEnumerable().Reverse().Where(l => !l.IsGroup).Select(l => (Guid?)l.ID)).ToList();
            if (_layers.Count == 0) _layers.Add(null);
            Layer.ItemsSource = _layers.Select(id => id is { } layer ? doc.Layers.First(l => l.ID == layer).Name : Localize.Text("Merged")).ToArray(); Layer.SelectedIndex = 0;
            // Calculations works on one channel, so its list starts at Red and offers Gray; Apply Image starts at the composite.
            var names = (_gray ? new[] { "Red", "Green", "Blue", "Gray", "Transparency" } : new[] { "RGB", "Red", "Green", "Blue", "Transparency" }).Select(Localize.Text).ToList();
            _alphas = Enumerable.Repeat<Guid?>(null, names.Count).Concat(doc.Channels.Select(c => (Guid?)c.ID)).ToList();
            Channel.ItemsSource = names.Concat(doc.Channels.Select(c => c.Name)).ToArray(); Channel.SelectedIndex = 0;
        }
        internal ChannelSource Read()
        {
            var index = Math.Max(0, Channel.SelectedIndex);
            var channel = _gray ? index switch { 0 => ImageChannel.Red, 1 => ImageChannel.Green, 2 => ImageChannel.Blue, 3 => ImageChannel.Gray, _ => ImageChannel.Alpha }
                : index switch { 0 => ImageChannel.RGB, 1 => ImageChannel.Red, 2 => ImageChannel.Green, 3 => ImageChannel.Blue, _ => ImageChannel.Alpha };
            return new ChannelSource(Doc, _layers[Math.Max(0, Layer.SelectedIndex)], channel, Invert.IsChecked == true, index < _alphas.Count ? _alphas[index] : null);
        }
        internal CanvasDocument Picked => Doc;
    }

    internal ChannelCalculationDialog(bool calculation, IReadOnlyList<(string Name, CanvasDocument Document)> documents, int current, string targetName, CheckBox preview)
    {
        Title = Localize.Text(calculation ? "Calculations" : "Apply Image"); SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var form = new Grid { ColumnDefinitions = new("70,240,*"), ColumnSpacing = 12, RowSpacing = 10, Width = 520 };
        var row = 0;
        void Put(Control c, int column, int span = 1) { while (form.RowDefinitions.Count <= row) form.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); Grid.SetRow(c, row); Grid.SetColumn(c, column); Grid.SetColumnSpan(c, span); c.VerticalAlignment = VerticalAlignment.Center; form.Children.Add(c); }
        TextBlock Right(string text) => new() { Text = Localize.Text(text), HorizontalAlignment = HorizontalAlignment.Right };
        void Rule() { Put(new Border { Height = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(0, 6) }, 0, 3); row++; }
        SourceRows Source(string label, char invertKey, string tag)
        {
            var rows = new SourceRows(documents, current, invertKey, calculation, tag);
            Put(Right(label), 0); Put(rows.Document, 1, 2); row++;
            Put(Right("Layer"), 0); Put(rows.Layer, 1, 2); row++;
            Put(Right("Channel"), 0); Put(rows.Channel, 1); Put(rows.Invert, 2); row++;
            LayerDialogControls.Key(this, invertKey, rows.Invert);
            return rows;
        }
        var first = Source(calculation ? "Source 1" : "Source", 'I', "source"); Rule();
        SourceRows? second = null;
        if (calculation) { second = Source("Source 2", 'V', "source 2"); Rule(); }
        else { Put(Right("Target"), 0); Put(new TextBlock { Text = targetName }, 1, 2); row++; }

        Blending = new ComboBox { Height = 30, Width = 240, Tag = "Blending" };
        Blending.ItemsSource = Blends.Select(b => (object)(b is { } blend ? new ComboBoxItem { Content = Localize.Text(BlendName(blend)), Tag = blend } : new ComboBoxItem { Content = new Separator(), IsEnabled = false, Height = 9, Padding = default })).ToArray();
        Blending.SelectedIndex = Array.IndexOf(Blends, ChannelBlend.Multiply);
        var opacity = LayerDialogControls.PercentField(100, "Opacity", out var opacityField);
        var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Localize.Text("Opacity") + "(O)", VerticalAlignment = VerticalAlignment.Center }, opacity } };
        LayerDialogControls.Key(this, 'O', opacityField);
        Put(Right("Blending"), 0); Put(Blending, 1); Put(opacityRow, 2); row++;
        // Add and Subtract take a scale and an offset, as in Photoshop.
        var scale = new ValueBox(1, 2, 1, "0.###", tag: "Scale"); var offset = new ValueBox(-255, 255, 0, tag: "Offset");
        var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new TextBlock { Text = Localize.Text("Scale") + ":", VerticalAlignment = VerticalAlignment.Center }, scale, new TextBlock { Text = Localize.Text("Offset") + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }, offset } };
        Put(scaleRow, 1, 2); row++;
        void Scales() { var blend = (Blending.SelectedItem as ComboBoxItem)?.Tag as ChannelBlend?; scaleRow.IsVisible = blend is ChannelBlend.Add or ChannelBlend.Subtract; }
        Blending.SelectionChanged += (_, _) => Scales(); Scales();
        var preserve = new CheckBox { Content = new TextBlock { Text = AdjustmentKit.Word("Preserve Transparency", "保留透明度") + "(T)" }, IsChecked = true, Tag = "Preserve Transparency" };
        var mask = new CheckBox { Content = new TextBlock { Text = Localize.Text("Mask") + "(K)…" }, Tag = "Mask" };
        LayerDialogControls.Key(this, 'T', preserve); LayerDialogControls.Key(this, 'K', mask);
        if (calculation) Put(mask, 1); else Put(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { preserve, mask } }, 1, 2);
        row++;
        // Mask…: the mask's own Image, Layer and Channel, shown when ticked.
        var maskStart = form.Children.Count;
        var maskRows = new SourceRows(documents, current, 'N', calculation, "mask");
        Put(Right("Image"), 0); Put(maskRows.Document, 1, 2); row++;
        Put(Right("Layer"), 0); Put(maskRows.Layer, 1, 2); row++;
        Put(Right("Channel"), 0); Put(maskRows.Channel, 1); Put(maskRows.Invert, 2); row++;
        var maskControls = form.Children.Skip(maskStart).ToArray();
        void Masked() { foreach (var c in maskControls) c.IsVisible = mask.IsChecked == true; }
        mask.IsCheckedChanged += (_, _) => Masked(); Masked();
        ComboBox? result = null;
        if (calculation)
        {
            Rule(); result = new ComboBox { ItemsSource = new[] { "New Document", "New Channel", "Selection" }.Select(Localize.Text).ToArray(), SelectedIndex = 1, Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "Result" };
            Put(Right("Result"), 0); Put(result, 1, 2); row++;
        }
        var error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Width = 520 };
        _build = () =>
        {
            var a = first.Read(); var b = second?.Read(); var m = mask.IsChecked == true ? maskRows.Read() : null; var target = documents[Math.Max(0, current)].Document;
            if (a.Document.Width != target.Width || a.Document.Height != target.Height || b is not null && (b.Document.Width != target.Width || b.Document.Height != target.Height)
                || m is not null && (m.Document.Width != target.Width || m.Document.Height != target.Height))
            { error.Text = Localize.Text("Source images must have the same pixel dimensions."); return null; }
            error.Text = "";
            var blend = (Blending.SelectedItem as ComboBoxItem)?.Tag as ChannelBlend? ?? ChannelBlend.Multiply;
            return new ChannelCalculationResult(a, b, new CalculationSettings(blend, (double)(opacityField.Value ?? 100) / 100, scale.Value, offset.Value, preserve.IsChecked == true), m, result?.SelectedIndex ?? 0);
        };
        var actions = AdjustmentKit.Actions(this, () => { if (_build() is { } built) { _result = built; Close(true); } }, () => Close(false), preview);
        // Every control reports a change, so the picture behind can follow.
        foreach (var combo in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(form).OfType<ComboBox>()) combo.SelectionChanged += (_, _) => Changed?.Invoke();
        foreach (var box in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(form).OfType<CheckBox>()) box.IsCheckedChanged += (_, _) => Changed?.Invoke();
        opacityField.ValueChanged += (_, _) => Changed?.Invoke(); scale.ValueChanged += () => Changed?.Invoke(); offset.ValueChanged += () => Changed?.Invoke();
        Content = AdjustmentKit.Body(new StackPanel { Spacing = 6, Children = { form, error } }, actions);
    }

    internal ChannelCalculationResult? Result => _result;
    private readonly Func<ChannelCalculationResult?> _build;
    /// <summary>What the dialog asks for as it stands, or null while the sources do not fit the target.</summary>
    internal ChannelCalculationResult? Current() => _build();
    internal event Action? Changed;
}
