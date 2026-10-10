using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Compositor.Desktop;

/// <summary>
/// The colours and layout styling logic of the interface, designed to strictly follow Adobe Photoshop's
/// professional Dark theme: neutral 0x32 dark grey chrome, 0x28 canvas pasteboard, 0x1473E6 Adobe blue accent,
/// 0x22 dark inset control surfaces, and crisp 1px borders and high-readability typography hierarchy.
/// </summary>
internal static class Skin
{
    // ---- Photoshop Layout & Surface Colours ----

    /// <summary>Photoshop dark UI chrome: panel bodies, tool headers, options bar and window frame (#323232).</summary>
    public static readonly Color Chrome = Color.FromRgb(0x32, 0x32, 0x32);

    /// <summary>Photoshop workspace pasteboard behind document canvas (#282828).</summary>
    public static readonly Color Pasteboard = Color.FromRgb(0x28, 0x28, 0x28);

    /// <summary>Deep recessed background for text inputs, number fields, listboxes, and slider tracks (#222222).</summary>
    public static readonly Color SurfaceDark = Color.FromRgb(0x22, 0x22, 0x22);

    /// <summary>Standard Photoshop button and toggle surface (#3D3D3D).</summary>
    public static readonly Color SurfaceControl = Color.FromRgb(0x3D, 0x3D, 0x3D);

    /// <summary>Photoshop control hover state (#4C4C4C).</summary>
    public static readonly Color SurfaceControlHover = Color.FromRgb(0x4C, 0x4C, 0x4C);

    /// <summary>Photoshop control pressed and active tool sunken state (#252525).</summary>
    public static readonly Color SurfaceControlPressed = Color.FromRgb(0x25, 0x25, 0x25);

    /// <summary>Subtle 1px border between docked workspace sections (#262626).</summary>
    public static readonly Color BorderSubtle = Color.FromRgb(0x26, 0x26, 0x26);

    /// <summary>Standard 1px control outline border (#464646).</summary>
    public static readonly Color BorderControl = Color.FromRgb(0x46, 0x46, 0x46);

    /// <summary>The transparency checker: Photoshop default neutral dark squares (#383838 and #454545).</summary>
    public static readonly Color CheckerBase = Color.FromRgb(0x38, 0x38, 0x38);
    public static readonly Color CheckerSquare = Color.FromRgb(0x45, 0x45, 0x45);
    /// <summary>How wide one checker square is, in points.</summary>
    public const double CheckerSize = 10;

    /// <summary>Photoshop hairline edge around document picture (#3F3F3F with subtle white glow).</summary>
    public static readonly Color PictureEdge = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);

    /// <summary>Photoshop's default cyan guide colour: srgb(0, 1, 1) at 0.9 (#E600FFFF).</summary>
    public static readonly Color Guide = Color.FromArgb(0xE6, 0x00, 0xFF, 0xFF);

    /// <summary>The layout grid's look — Photoshop Light Gray grid with subtle subdivisions.</summary>
    public static readonly Color Grid = Color.FromArgb(0x73, 0xB3, 0xB3, 0xB3);
    public static readonly Color GridFine = Color.FromArgb(0x47, 0xB3, 0xB3, 0xB3);

    /// <summary>Photoshop pixel grid: subtle line around document pixels at high zoom.</summary>
    public static readonly Color PixelGrid = Color.FromArgb(0x73, 0x8C, 0x8C, 0x8C);

    /// <summary>Photoshop crop dim overlay: 60% black.</summary>
    public static readonly Color CropDim = Color.FromArgb(0x99, 0x00, 0x00, 0x00);

    /// <summary>Photoshop ruler face, tick, label and edge (#2C2C2C, #808080, #B0B0B0, #1E1E1E).</summary>
    public static readonly Color RulerFace = Color.FromRgb(0x2C, 0x2C, 0x2C);
    public static readonly Color RulerTick = Color.FromRgb(0x80, 0x80, 0x80);
    public static readonly Color RulerLabel = Color.FromRgb(0xB0, 0xB0, 0xB0);
    public static readonly Color RulerEdge = Color.FromRgb(0x1E, 0x1E, 0x1E);

    // ---- Adobe Photoshop Accent & Typography Colours ----

    /// <summary>Adobe Photoshop signature accent blue (#1473E6).</summary>
    public static readonly Color Accent = Color.FromRgb(0x14, 0x73, 0xE6);
    /// <summary>Photoshop accent hover (#378EF0).</summary>
    public static readonly Color AccentHover = Color.FromRgb(0x37, 0x8E, 0xF0);
    /// <summary>Photoshop accent pressed (#0D5BBD).</summary>
    public static readonly Color AccentPressed = Color.FromRgb(0x0D, 0x5B, 0xBD);

    /// <summary>Photoshop primary text label: clean light grey (#E1E1E1).</summary>
    public static readonly Color Label = Color.FromRgb(0xE1, 0xE1, 0xE1);
    /// <summary>Photoshop secondary text label: neutral medium grey (#9E9E9E).</summary>
    public static readonly Color Secondary = Color.FromRgb(0x9E, 0x9E, 0x9E);
    /// <summary>Photoshop disabled text: muted dark grey (#666666).</summary>
    public static readonly Color Disabled = Color.FromRgb(0x66, 0x66, 0x66);

    // ---- App Surface Brushes ----

    public static readonly IBrush ChromeBrush = new SolidColorBrush(Chrome);
    public static readonly IBrush PasteboardBrush = new SolidColorBrush(Pasteboard);
    public static readonly IBrush SurfaceDarkBrush = new SolidColorBrush(SurfaceDark);
    public static readonly IBrush SurfaceControlBrush = new SolidColorBrush(SurfaceControl);
    public static readonly IBrush SurfaceControlHoverBrush = new SolidColorBrush(SurfaceControlHover);
    public static readonly IBrush SurfaceControlPressedBrush = new SolidColorBrush(SurfaceControlPressed);
    public static readonly IBrush BorderSubtleBrush = new SolidColorBrush(BorderSubtle);
    public static readonly IBrush BorderControlBrush = new SolidColorBrush(BorderControl);
    public static readonly IBrush LabelBrush = new SolidColorBrush(Label);
    public static readonly IBrush SecondaryBrush = new SolidColorBrush(Secondary);
    public static readonly IBrush DisabledBrush = new SolidColorBrush(Disabled);
    public static readonly IBrush AccentBrush = new SolidColorBrush(Accent);

    /// <summary>Photoshop active document tab: seamless #323232 body with #1473E6 top accent border.</summary>
    public static readonly IBrush TabFront = new SolidColorBrush(Chrome);
    /// <summary>Photoshop inactive document tab: darker #252525 body.</summary>
    public static readonly IBrush TabBack = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
    public static readonly IBrush TabFrontEdge = new SolidColorBrush(Accent);
    public static readonly IBrush TabBackEdge = new SolidColorBrush(BorderSubtle);

    /// <summary>Photoshop menu separator rule: #3E3E3E.</summary>
    public static readonly IBrush MenuRule = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x3E));

    /// <summary>Pens for canvas drawing.</summary>
    public static readonly IPen PictureEdgePen = new Pen(new SolidColorBrush(PictureEdge), 1);
    public static readonly IPen GuidePen = new Pen(new SolidColorBrush(Guide), 1);
    public static readonly IPen SnapPen = new Pen(new SolidColorBrush(Accent), 1);
    public static readonly IPen GridPen = new Pen(new SolidColorBrush(Grid), 1);
    public static readonly IPen GridFinePen = new Pen(new SolidColorBrush(GridFine), 1);
    public static readonly IPen PixelGridPen = new Pen(new SolidColorBrush(PixelGrid), 1);
    public static readonly IBrush CropDimBrush = new SolidColorBrush(CropDim);

    /// <summary>Photoshop transform box: crisp Adobe blue line with solid white handles.</summary>
    public static readonly IPen TransformPen = new Pen(new SolidColorBrush(Accent), 1);
    public static readonly IBrush HandleFill = Brushes.White;
    public static readonly IPen HandlePen = new Pen(new SolidColorBrush(Accent), 1);

    /// <summary>Photoshop curve editor ground and grid.</summary>
    public static readonly IBrush CurveGround = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
    public static readonly IBrush CurveGrid = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));

    private static IBrush? _checker;

    /// <summary>
    /// The transparency checker as a tiled brush: the Mac tiles it in screen points whatever the zoom is, so
    /// this is a 20-point tile of four squares rather than anything drawn per pixel.
    /// </summary>
    public static IBrush Checker
    {
        get
        {
            if (_checker is not null) return _checker;
            var size = (int)(CheckerSize * 2);
            var tile = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (var context = tile.CreateDrawingContext())
            {
                var half = size / 2.0;
                context.FillRectangle(new SolidColorBrush(CheckerBase), new Rect(0, 0, size, size));
                var square = new SolidColorBrush(CheckerSquare);
                context.FillRectangle(square, new Rect(0, 0, half, half));
                context.FillRectangle(square, new Rect(half, half, half, half));
            }
            return _checker = new ImageBrush(tile)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.Fill,
                DestinationRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
        }
    }

    /// <summary>
    /// Moves Fluent's dark palette onto the Mac's colours where the two differ. The accent is not spelled out
    /// per control — a hundred keys hold a shade of Fluent's own blue — so the ones that follow the accent are
    /// found by the shade they hold rather than by name, and each is written back in the type it was found in
    /// (some keys are colours, some are brushes) so nothing downstream is handed the wrong kind of value.
    /// Returns how many resources were moved, which is what the self check reads.
    /// </summary>
    public static int Apply(FluentTheme theme)
    {
        if (theme.Resources is not ResourceDictionary resources
            || !resources.ThemeDictionaries.TryGetValue(ThemeVariant.Dark, out var provider)
            || provider is not ResourceDictionary dark) return 0;

        var shades = new Dictionary<Color, Color>
        {
            [Color.FromRgb(0x00, 0x78, 0xD4)] = Accent,
            [Color.FromRgb(0x23, 0xA0, 0xFF)] = AccentHover,
            [Color.FromRgb(0x00, 0x58, 0x9B)] = AccentPressed,
        };
        var moved = 0;
        // Read through TryGetResource rather than the indexer: a resource dictionary answers that one and not
        // the other, and only the try has the variant in hand.
        foreach (var key in dark.Keys.ToList())
        {
            if (!dark.TryGetResource(key, null, out var current)) continue;
            if (Tint(current) is not { } colour || !shades.TryGetValue(colour, out var replacement)) continue;
            dark[key] = Wear(current, replacement);
            moved++;
        }

        // Photoshop's chrome, its primary label and secondary text
        moved += Wear(dark, "SystemControlBackgroundAltHighBrush", Chrome);
        moved += Wear(dark, "SystemControlForegroundBaseHighBrush", Label);
        moved += Wear(dark, "SystemControlForegroundBaseMediumBrush", Secondary);

        // Photoshop control surfaces and borders
        moved += Wear(dark, "ButtonBackground", SurfaceControl);
        moved += Wear(dark, "ButtonBackgroundPointerOver", SurfaceControlHover);
        moved += Wear(dark, "ButtonBackgroundPressed", SurfaceControlPressed);
        moved += Wear(dark, "ButtonBorderBrush", BorderControl);
        moved += Wear(dark, "ButtonForeground", Label);

        moved += Wear(dark, "TextControlBackground", SurfaceDark);
        moved += Wear(dark, "TextControlBackgroundPointerOver", Color.FromRgb(0x28, 0x28, 0x28));
        moved += Wear(dark, "TextControlBackgroundFocused", SurfaceDark);
        moved += Wear(dark, "TextControlBorderBrush", BorderControl);
        moved += Wear(dark, "TextControlBorderBrushFocused", Accent);
        moved += Wear(dark, "TextControlForeground", Label);

        moved += Wear(dark, "ComboBoxBackground", SurfaceControl);
        moved += Wear(dark, "ComboBoxBackgroundPointerOver", SurfaceControlHover);
        moved += Wear(dark, "ComboBoxBorderBrush", BorderControl);

        moved += Wear(dark, "ListBoxBackground", SurfaceDark);
        moved += Wear(dark, "SliderTrackValueFill", Accent);
        moved += Wear(dark, "SliderThumbBackground", Color.FromRgb(0xA6, 0xA6, 0xA6));
        moved += Wear(dark, "SliderThumbBackgroundPointerOver", Color.FromRgb(0xD0, 0xD0, 0xD0));
        moved += Wear(dark, "CheckBoxCheckBackgroundFillChecked", Accent);
        moved += Wear(dark, "RadioButtonOuterEllipseFillChecked", Accent);

        return moved;
    }

    /// <summary>The colour a resource holds, whether it is a colour or a brush of one.</summary>
    private static Color? Tint(object? value) => value switch
    {
        Color colour => colour,
        ISolidColorBrush brush => brush.Color,
        _ => null,
    };

    /// <summary>The same kind of value with the colour changed.</summary>
    private static object Wear(object? original, Color colour) => original switch
    {
        Color => colour,
        ImmutableSolidColorBrush => new ImmutableSolidColorBrush(colour),
        ISolidColorBrush => new SolidColorBrush(colour),
        _ => new SolidColorBrush(colour),
    };

    private static int Wear(ResourceDictionary dark, string key, Color colour)
    {
        if (!dark.TryGetResource(key, null, out var original)) return 0;
        dark[key] = Wear(original, colour);
        return 1;
    }
}
