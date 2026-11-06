using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Compositor.Core.Document;
using ImageMagick;

namespace Compositor.Desktop;

/// <summary>
/// The app's color picker, laid out as Photoshop's: a field and a strip that show whichever of H, S, B, R, G, B, L, a or b
/// is chosen, the new color above the current one, and the amounts in all four models. It is a window of its own rather
/// than a dialog that takes the editor over: the canvas stays live so a click on it can sample the color under the pointer.
/// </summary>
internal sealed class ColorPickerDialog : DialogWindow
{
    private const double FieldSize = 300;
    private enum Axis { Hue, Saturation, Brightness, Red, Green, Blue, L, A, B }

    private readonly PickerHsb _hsb;
    private readonly (double Red, double Green, double Blue) _start;
    private Axis _axis = Axis.Hue;
    private readonly ColorField _field;
    private readonly ColorStrip _hue;
    private readonly Border _new = new() { Width = 72, Height = 44, Tag = "picker-new" };
    private readonly Button _current = new() { Width = 72, Height = 44, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(0), Tag = "picker-current" };
    private readonly Dictionary<string, NumericUpDown> _amounts = [];
    private readonly TextBox _hex = DialogKit.Line("picker-hex");
    private readonly CheckBox _webOnly = new() { Content = Localize.Text("Only Web Colors"), Tag = "Only Web Colors" };
    private bool _showing;
    private bool _done;

    /// <summary>Every color the picker is moved to, which is what lets the sheet behind it preview what that color would do.</summary>
    public event Action<(double Red, double Green, double Blue)>? Moved;

    /// <summary>OK: the color the picker ended on.</summary>
    public event Action<(double Red, double Green, double Blue)>? Applied;

    /// <summary>Cancel, or the window shut: nothing is to be taken.</summary>
    public event Action? Cancelled;

    /// <summary>Add to Swatches, which the Swatches panel keeps.</summary>
    internal static event Action<(double Red, double Green, double Blue)>? SwatchAdded;

    /// <summary>
    /// Opens the picker on a color the panel behind it is already showing, and reports every color it is moved to. A Cancel
    /// reports the color it started on, which puts the panel back.
    /// </summary>
    public static async Task Pick(Window owner, string title, (double Red, double Green, double Blue) start,
        Action<(double Red, double Green, double Blue)> changed)
    {
        var picker = new ColorPickerDialog(title, start);
        picker.Moved += changed;
        picker.Cancelled += () => changed(start);
        await picker.ShowDialog(owner);
    }

    public ColorPickerDialog(string title, (double Red, double Green, double Blue) start)
    {
        Title = Localize.Text(title);
        Width = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _start = start;
        _hsb = new PickerHsb(start);
        _field = new ColorField(this) { Tag = "picker-field" };
        _hue = new ColorStrip(this) { Tag = "picker-strip" };

        Button Action(string text, Action act, bool accent = false)
        {
            var button = FormDialog.ActionButton(text, act); button.Margin = default; button.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (accent) { button.Background = Skin.AccentBrush; button.BorderBrush = Skin.AccentBrush; button.Foreground = Brushes.White; }
            return button;
        }
        Ok = Action("OK", () => { _done = true; Applied?.Invoke(Colour); Close(); }, true); Ok.IsDefault = true;
        Cancel = Action("Cancel", Close); Cancel.IsCancel = true;
        var addSwatch = Action("Add to Swatches", () => SwatchAdded?.Invoke(Colour));
        var library = Action("Color Libraries", () => { });
        library.Flyout = LibraryFlyout();
        var actions = new StackPanel { Spacing = 10, Width = 130, Children = { Ok, Cancel, new Border { Height = 6 }, addSwatch, library } };

        _current.Background = Brush(start); _current.Click += (_, _) => { _hsb.SetRgb(start); Refresh(); };
        ToolTip.SetTip(_current, Localize.Text("Click to go back to the original color"));
        var swatches = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = Localize.IsChinese ? "新的" : "new", HorizontalAlignment = HorizontalAlignment.Center, Foreground = Skin.SecondaryBrush, Margin = new Thickness(0, 0, 0, 2) },
                new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = new StackPanel { Children = { _new, _current } } },
                new TextBlock { Text = Localize.IsChinese ? "当前" : "current", HorizontalAlignment = HorizontalAlignment.Center, Foreground = Skin.SecondaryBrush, Margin = new Thickness(0, 2, 0, 0) },
            },
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Children = { swatches } };
        swatches.HorizontalAlignment = HorizontalAlignment.Left; swatches.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(actions, 1); top.Children.Add(actions);

        var models = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 22, RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        Control Column(params Control[] rows) { var column = new StackPanel { Spacing = 5 }; foreach (var row in rows) column.Children.Add(row); return column; }
        var hsb = Column(Radio("H", Axis.Hue, 360, "°", true), Radio("S", Axis.Saturation, 100, "%"), Radio("B", Axis.Brightness, 100, "%"));
        var lab = Column(Radio("L", Axis.L, 100, ""), Radio("a", Axis.A, 127, ""), Radio("b", Axis.B, 127, ""));
        var rgb = Column(Radio("R", Axis.Red, 255, ""), Radio("G", Axis.Green, 255, ""), Radio("B", Axis.Blue, 255, ""));
        var cmyk = Column(Amount("C", "C", "%"), Amount("M", "M", "%"), Amount("Y", "Y", "%"), Amount("K", "K", "%"));
        Grid.SetColumn(lab, 1); Grid.SetRow(rgb, 1); Grid.SetRow(cmyk, 1); Grid.SetColumn(cmyk, 1);
        models.Children.Add(hsb); models.Children.Add(lab); models.Children.Add(rgb); models.Children.Add(cmyk);

        _hex.Width = 96; _hex.FontFamily = new FontFamily("Consolas,Menlo,monospace");
        _hex.KeyDown += (_, pressed) => { if (pressed.Key == Key.Enter) { CommitHex(); pressed.Handled = true; } };
        _hex.LostFocus += (_, _) => CommitHex();
        var hex = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(26, 8, 0, 0), Children = { new TextBlock { Text = "#", VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush }, _hex } };
        _webOnly.IsCheckedChanged += (_, _) => Refresh();
        Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8, Margin = new Thickness(16, 8, 16, 16),
            Children =
            {
                new StackPanel { Spacing = 10, Children = { _field, _webOnly } },
                Placed(_hue, 1),
                Placed(new StackPanel { Children = { top, models, hex, new TextBlock { Text = Localize.Text("Click the canvas to sample"), Foreground = Skin.SecondaryBrush, FontSize = 11, Margin = new Thickness(26, 8, 0, 0) } } }, 2),
            },
        };
        Refresh();
        Closed += (_, _) => { if (!_done) Cancelled?.Invoke(); };
    }
    private static Control Placed(Control control, int column) { Grid.SetColumn(control, column); return control; }

    /// <summary>The web-safe colors, picked straight from a grid, which is the library this app has.</summary>
    private Flyout LibraryFlyout()
    {
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 18, Width = 18 * 14 };
        for (var r = 0; r < 6; r++) for (var g = 0; g < 6; g++) for (var b = 0; b < 6; b++)
        {
            (double Red, double Green, double Blue) color = (r / 5d, g / 5d, b / 5d);
            var chip = new Button { Width = 14, Height = 14, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(0), Background = Brush(color) };
            chip.Click += (_, _) => { _hsb.SetRgb(color); Refresh(); };
            grid.Children.Add(chip);
        }
        return new Flyout { Content = grid, Placement = PlacementMode.Left };
    }

    private Control Radio(string name, Axis axis, double most, string unit, bool chosen = false)
    {
        var radio = new RadioButton { GroupName = "picker-axis", IsChecked = chosen, Tag = "picker-axis-" + axis, MinWidth = 0, Content = new TextBlock { Text = name + ":", Width = 16 } };
        radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) { _axis = axis; _field.Show(); _hue.Show(); } };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { radio, NumberField(axis.ToString(), most, axis is Axis.A or Axis.B ? -128 : 0, unit) } };
    }
    private Control Amount(string name, string key, string unit) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(32, 0, 0, 0), Children = { new TextBlock { Text = name + ":", Width = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush }, NumberField(key, 100, 0, unit) } };
    private Control NumberField(string key, double most, double least, string unit)
    {
        var amount = new NumericUpDown { Minimum = (decimal)least, Maximum = (decimal)most, Increment = 1, Width = 62, Height = 26, MinHeight = 0, FormatString = "0", ShowButtonSpinner = false, ClipValueToMinMax = true, VerticalContentAlignment = VerticalAlignment.Center, Tag = "picker-" + key };
        amount.ValueChanged += (_, _) => { if (!_showing && amount.Value is { } value) Typed(key, (double)value); };
        _amounts[key] = amount;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { amount, new TextBlock { Text = unit, Width = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush } } };
    }
    /// <summary>A number typed in one model moves the color; the others follow.</summary>
    private void Typed(string key, double value)
    {
        var rgb = Colour; var lab = ToLab(rgb);
        switch (key)
        {
            case "Hue": _hsb.Hue = value; break;
            case "Saturation": _hsb.Saturation = value / 100; break;
            case "Brightness": _hsb.Brightness = value / 100; break;
            case "Red": _hsb.SetRgb((value / 255, rgb.Green, rgb.Blue)); break;
            case "Green": _hsb.SetRgb((rgb.Red, value / 255, rgb.Blue)); break;
            case "Blue": _hsb.SetRgb((rgb.Red, rgb.Green, value / 255)); break;
            case "L": _hsb.SetRgb(FromLab((value, lab.A, lab.B))); break;
            case "A": _hsb.SetRgb(FromLab((lab.L, value, lab.B))); break;
            case "B": _hsb.SetRgb(FromLab((lab.L, lab.A, value))); break;
            default:
                double C = Read("C"), M = Read("M"), Y = Read("Y"), K = Read("K");
                _hsb.SetRgb(((1 - C) * (1 - K), (1 - M) * (1 - K), (1 - Y) * (1 - K))); break;
        }
        Refresh();
    }
    private double Read(string key) => (double)(_amounts[key].Value ?? 0) / 100;

    /// <summary>The color the picker is on.</summary>
    internal (double Red, double Green, double Blue) Colour => _webOnly.IsChecked == true ? WebSafe(_hsb.Rgb) : _hsb.Rgb;
    private static (double, double, double) WebSafe((double Red, double Green, double Blue) c) => (Math.Round(c.Red * 5) / 5, Math.Round(c.Green * 5) / 5, Math.Round(c.Blue * 5) / 5);

    /// <summary>The field a pointer drags over, and the strip beside it: the checks click these.</summary>
    internal Control Field => _field;
    internal Control Hue => _hue;

    /// <summary>The two buttons, which the checks press rather than going through the key table.</summary>
    internal Button Ok { get; }
    internal Button Cancel { get; }

    /// <summary>Takes a color sampled from the canvas: the field moves to it and the numbers follow.</summary>
    public void Sample((double Red, double Green, double Blue) colour) { _hsb.SetRgb(colour); Refresh(); }

    private static SolidColorBrush Brush((double Red, double Green, double Blue) c) => new(Color.FromRgb(Byte(c.Red), Byte(c.Green), Byte(c.Blue)));
    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    /// <summary>The field's mark, the strip's arrows, the swatch and all the numbers, from the one place so they can't disagree.</summary>
    private void Refresh()
    {
        _field.Show(); _hue.Show();
        var colour = Colour;
        _new.Background = Brush(colour);
        _showing = true;
        try
        {
            void Put(string key, double value) => _amounts[key].Value = (decimal)Math.Clamp(Math.Round(value), (double)_amounts[key].Minimum, (double)_amounts[key].Maximum);
            Put("Hue", _hsb.Hue); Put("Saturation", _hsb.Saturation * 100); Put("Brightness", _hsb.Brightness * 100);
            Put("Red", colour.Red * 255); Put("Green", colour.Green * 255); Put("Blue", colour.Blue * 255);
            var lab = ToLab(colour); Put("L", lab.L); Put("A", lab.A); Put("B", lab.B);
            var cmyk = ToCmyk(colour); Put("C", cmyk.C); Put("M", cmyk.M); Put("Y", cmyk.Y); Put("K", cmyk.K);
            _hex.Text = PickerHsb.Text(colour);
        }
        finally { _showing = false; }
        Moved?.Invoke(colour);
    }

    /// <summary>The hex box typed into: the color follows it, or the box goes back to what the color is.</summary>
    private void CommitHex()
    {
        if (_showing) return;
        if (PickerHsb.FromHex(_hex.Text ?? "") is { } parsed) { _hsb.SetRgb(parsed); Refresh(); return; }
        _showing = true; try { _hex.Text = PickerHsb.Text(Colour); } finally { _showing = false; }
    }

    private static double Linear(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
    private static double Gamma(double v) => Math.Clamp(v <= .0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - .055, 0, 1);
    /// <summary>sRGB to Lab under D50, the white Photoshop's Lab uses.</summary>
    private static (double L, double A, double B) ToLab((double Red, double Green, double Blue) c)
    {
        double r = Linear(c.Red), g = Linear(c.Green), b = Linear(c.Blue);
        static double F(double t) => t > 216d / 24389 ? Math.Cbrt(t) : (24389d / 27 * t + 16) / 116;
        double x = F((.4360747 * r + .3850649 * g + .1430804 * b) / .96422), y = F(.2225045 * r + .7168786 * g + .0606169 * b), z = F((.0139322 * r + .0971045 * g + .7141733 * b) / .82521);
        return (116 * y - 16, 500 * (x - y), 200 * (y - z));
    }
    private static (double, double, double) FromLab((double L, double A, double B) lab)
    {
        var fy = (lab.L + 16) / 116; var fx = fy + lab.A / 500; var fz = fy - lab.B / 200;
        static double T(double f) => f * f * f > 216d / 24389 ? f * f * f : (116 * f - 16) * 27 / 24389;
        double x = T(fx) * .96422, y = T(fy), z = T(fz) * .82521;
        return (Gamma(3.1338561 * x - 1.6168667 * y - .4906146 * z), Gamma(-.9787684 * x + 1.9161415 * y + .0334540 * z), Gamma(.0719453 * x - .2289914 * y + 1.4052427 * z));
    }
    private static readonly Dictionary<int, (double, double, double, double)> Cmyks = [];
    /// <summary>The ink amounts through the default print profile, as Photoshop shows them; plain arithmetic when that can't run.</summary>
    private static (double C, double M, double Y, double K) ToCmyk((double Red, double Green, double Blue) c)
    {
        var key = Byte(c.Red) << 16 | Byte(c.Green) << 8 | Byte(c.Blue);
        if (Cmyks.TryGetValue(key, out var known)) return known;
        (double, double, double, double) result;
        try
        {
            using var image = new MagickImage(MagickColor.FromRgb(Byte(c.Red), Byte(c.Green), Byte(c.Blue)), 1, 1);
            image.TransformColorSpace(ColorProfiles.SRGB, ColorProfiles.USWebCoatedSWOP);
            using var pixels = image.GetPixels();
            var ink = pixels.GetValue(0, 0)!;
            result = (ink[0] / (double)Quantum.Max * 100, ink[1] / (double)Quantum.Max * 100, ink[2] / (double)Quantum.Max * 100, ink[3] / (double)Quantum.Max * 100);
        }
        catch (MagickException)
        {
            var k = 1 - Math.Max(c.Red, Math.Max(c.Green, c.Blue)); var left = Math.Max(1e-9, 1 - k);
            result = ((1 - c.Red - k) / left * 100, (1 - c.Green - k) / left * 100, (1 - c.Blue - k) / left * 100, k * 100);
        }
        if (Cmyks.Count > 4096) Cmyks.Clear();
        return Cmyks[key] = result;
    }

    /// <summary>How far along the strip the color is, and where in the field, for the chosen axis: each 0 to 1.</summary>
    private double StripAt() => _axis switch
    {
        Axis.Hue => _hsb.Hue / 360, Axis.Saturation => _hsb.Saturation, Axis.Brightness => _hsb.Brightness,
        Axis.Red => _hsb.Rgb.Red, Axis.Green => _hsb.Rgb.Green, Axis.Blue => _hsb.Rgb.Blue,
        Axis.L => ToLab(_hsb.Rgb).L / 100, Axis.A => (ToLab(_hsb.Rgb).A + 128) / 255, _ => (ToLab(_hsb.Rgb).B + 128) / 255,
    };
    private (double X, double Y) FieldAt()
    {
        var rgb = _hsb.Rgb; var lab = ToLab(rgb);
        return _axis switch
        {
            Axis.Hue => (_hsb.Saturation, _hsb.Brightness), Axis.Saturation => (_hsb.Hue / 360, _hsb.Brightness), Axis.Brightness => (_hsb.Hue / 360, _hsb.Saturation),
            Axis.Red => (rgb.Blue, rgb.Green), Axis.Green => (rgb.Blue, rgb.Red), Axis.Blue => (rgb.Red, rgb.Green),
            Axis.L => ((lab.A + 128) / 255, (lab.B + 128) / 255), Axis.A => ((lab.B + 128) / 255, lab.L / 100), _ => ((lab.A + 128) / 255, lab.L / 100),
        };
    }
    /// <summary>The color at a place in the field with the strip at <paramref name="z"/>.</summary>
    private (double Red, double Green, double Blue) ColorAt(double x, double y, double z) => _axis switch
    {
        Axis.Hue => new PickerHsb(z * 360, x, y).Rgb, Axis.Saturation => new PickerHsb(x * 360, z, y).Rgb, Axis.Brightness => new PickerHsb(x * 360, y, z).Rgb,
        Axis.Red => (z, y, x), Axis.Green => (y, z, x), Axis.Blue => (x, y, z),
        Axis.L => FromLab((z * 100, x * 255 - 128, y * 255 - 128)), Axis.A => FromLab((y * 100, z * 255 - 128, x * 255 - 128)), _ => FromLab((y * 100, x * 255 - 128, z * 255 - 128)),
    };
    private void MoveTo(double x, double y, double z)
    {
        if (_axis == Axis.Hue) { _hsb.Hue = z * 360; _hsb.Saturation = x; _hsb.Brightness = y; }
        else if (_axis == Axis.Saturation) { _hsb.Hue = x * 360; _hsb.Saturation = z; _hsb.Brightness = y; }
        else if (_axis == Axis.Brightness) { _hsb.Hue = x * 360; _hsb.Saturation = y; _hsb.Brightness = z; }
        else _hsb.SetRgb(ColorAt(x, y, z));
        Refresh();
    }

    /// <summary>Paints a picture of size × size (or 1 × size for the strip), each pixel from <paramref name="color"/>.</summary>
    private static unsafe void Paint(WriteableBitmap bitmap, Func<int, int, (double Red, double Green, double Blue)> color)
    {
        using var buffer = bitmap.Lock();
        var size = bitmap.PixelSize;
        for (var y = 0; y < size.Height; y++)
        {
            var row = (uint*)(buffer.Address + y * buffer.RowBytes);
            for (var x = 0; x < size.Width; x++) { var c = color(x, y); row[x] = 0xFF000000u | (uint)Byte(c.Red) << 16 | (uint)Byte(c.Green) << 8 | Byte(c.Blue); }
        }
    }

    /// <summary>The two axes that aren't the strip's, as a picture with a ring where the color is.</summary>
    private sealed class ColorField : Control
    {
        private const int Cells = 96;
        private readonly ColorPickerDialog _owner;
        private readonly WriteableBitmap _picture = new(new PixelSize(Cells, Cells), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        public ColorField(ColorPickerDialog owner) { _owner = owner; Width = FieldSize; Height = FieldSize; ClipToBounds = true; }
        internal void Show()
        {
            var z = _owner.StripAt();
            Paint(_picture, (x, y) => _owner.ColorAt(x / (Cells - 1d), 1 - y / (Cells - 1d), z));
            InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            var box = new Rect(Bounds.Size);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality })) context.DrawImage(_picture, new Rect(0, 0, Cells, Cells), box);
            var place = _owner.FieldAt(); var at = new Point(place.X * Bounds.Width, (1 - place.Y) * Bounds.Height);
            context.DrawEllipse(null, new Pen(Brushes.White, 1.5), at, 6, 6);
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Colors.Black, .75), .75), at, 6.75, 6.75);
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Pointer.Capture(this); Take(e.GetPosition(this)); }
        protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (e.Pointer.Captured == this) Take(e.GetPosition(this)); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); e.Pointer.Capture(null); }
        private void Take(Point at) => _owner.MoveTo(Math.Clamp(at.X / Math.Max(1, Bounds.Width), 0, 1), 1 - Math.Clamp(at.Y / Math.Max(1, Bounds.Height), 0, 1), _owner.StripAt());
    }

    /// <summary>The chosen axis from one end to the other, with an arrow each side of where the color is.</summary>
    private sealed class ColorStrip : Control
    {
        private const int Cells = 180;
        private readonly ColorPickerDialog _owner;
        private readonly WriteableBitmap _picture = new(new PixelSize(1, Cells), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        public ColorStrip(ColorPickerDialog owner) { _owner = owner; Width = 34; Height = FieldSize; VerticalAlignment = VerticalAlignment.Top; }
        internal void Show()
        {
            var place = _owner.FieldAt();
            // The hue strip stays pure, as Photoshop's does, whatever the saturation and brightness.
            Paint(_picture, _owner._axis == Axis.Hue ? (_, y) => new PickerHsb((1 - y / (Cells - 1d)) * 360, 1, 1).Rgb : (_, y) => _owner.ColorAt(place.X, place.Y, 1 - y / (Cells - 1d)));
            InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            context.DrawImage(_picture, new Rect(0, 0, 1, Cells), new Rect(7, 0, 20, Bounds.Height));
            var at = (1 - _owner.StripAt()) * Bounds.Height;
            context.DrawGeometry(Skin.LabelBrush, null, Arrow(0, at - 5, true));
            context.DrawGeometry(Skin.LabelBrush, null, Arrow(27, at - 5, false));
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Pointer.Capture(this); Take(e.GetPosition(this)); }
        protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (e.Pointer.Captured == this) Take(e.GetPosition(this)); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); e.Pointer.Capture(null); }
        private void Take(Point at)
        {
            var place = _owner.FieldAt();
            _owner.MoveTo(place.X, place.Y, 1 - Math.Clamp(at.Y / Math.Max(1, Bounds.Height), 0, 1));
        }
        /// <summary>A small triangle pointing at the strip, on one side of it or the other.</summary>
        private static StreamGeometry Arrow(double x, double y, bool towardsRight)
        {
            var geometry = new StreamGeometry();
            using var path = geometry.Open();
            var tip = towardsRight ? x + 7 : x; var back = towardsRight ? x : x + 7;
            path.BeginFigure(new Point(back, y), true); path.LineTo(new Point(tip, y + 5)); path.LineTo(new Point(back, y + 10)); path.EndFigure(true);
            return geometry;
        }
    }
}
