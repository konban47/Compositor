using System.Globalization;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Jint;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Compositor.Core.IO;

/// <summary>What a script did: whether it changed the document, the error that stopped it, and what it said.</summary>
public sealed record ScriptOutcome(bool Changed, string? Error, IReadOnlyList<string> Alerts, IReadOnlyList<string> Output);

/// <summary>
/// Runs JavaScript and ExtendScript (.js, .jsx) files the way Photoshop scripts reach a document, on the Jint
/// interpreter: <c>app</c>, <c>app.activeDocument</c> with its layers and File Info, <c>alert</c> and <c>$.writeln</c>.
/// Scripts get no file, network or .NET access, and are stopped past ten seconds, 64 MB or deep recursion.
/// ExtendScript's compiled .jsxbin and its File, Folder and ActionDescriptor objects are not available.
/// </summary>
public static class ScriptHost
{
    public const int MaxSourceBytes = 1024 * 1024;

    public static ScriptOutcome Run(string source, CanvasDocument? document, Guid? activeLayer, string documentName = "Untitled",
        CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        if (source.Length > MaxSourceBytes) return new(false, "The script is too large.", [], []);
        var alerts = new List<string>(); var output = new List<string>();
        var application = new ScriptApplication(document, activeLayer, documentName);
        var engine = new Engine(options =>
        {
            options.LimitMemory(64_000_000);
            options.TimeoutInterval(timeout ?? TimeSpan.FromSeconds(10));
            options.MaxStatements(10_000_000);
            options.LimitRecursion(256);
            options.CancellationToken(cancellation);
            // Members are matched without regard to case, so ExtendScript's camelCase reaches these objects.
            options.SetTypeResolver(new TypeResolver { MemberNameComparer = StringComparer.OrdinalIgnoreCase });
            options.Interop.AllowGetType = false;
            options.Interop.AllowSystemReflection = false;
        });
        engine.SetValue("app", application);
        engine.SetValue("alert", new Action<object?>(message => { if (alerts.Count < 100) alerts.Add(Text(message)); }));
        engine.SetValue("$", new ScriptDollar(output));
        // ExtendScript's enumerations, as strings the layer setters accept.
        engine.Execute("var BlendMode = {NORMAL:'NORMAL',MULTIPLY:'MULTIPLY',SCREEN:'SCREEN',OVERLAY:'OVERLAY',SOFTLIGHT:'SOFTLIGHT',HARDLIGHT:'HARDLIGHT',"
            + "DARKEN:'DARKEN',LIGHTEN:'LIGHTEN',DIFFERENCE:'DIFFERENCE',EXCLUSION:'EXCLUSION',COLORDODGE:'COLORDODGE',COLORBURN:'COLORBURN',"
            + "HUE:'HUE',SATURATION:'SATURATION',COLORBLEND:'COLORBLEND',LUMINOSITY:'LUMINOSITY'};"
            + "var Units = {PIXELS:'PIXELS'}; var DocumentMode = {RGB:'RGB',CMYK:'CMYK',GRAYSCALE:'GRAYSCALE',LAB:'LAB'};");
        try
        {
            engine.Execute(source);
            return new(application.Changed, null, alerts, output);
        }
        catch (JavaScriptException error) when (error.Error.IsObject() && error.Error.AsObject().Get("name").ToString() == "SyntaxError")
        { return new(false, $"The script could not be read: {error.Message}", alerts, output); }
        catch (JavaScriptException error) { return new(application.Changed, $"{error.Message} (line {error.Location.Start.Line})", alerts, output); }
        catch (ScriptException error) { return new(application.Changed, error.Message, alerts, output); }
        catch (TimeoutException) { return new(application.Changed, "The script ran too long and was stopped.", alerts, output); }
        catch (MemoryLimitExceededException) { return new(application.Changed, "The script used too much memory and was stopped.", alerts, output); }
        catch (StatementsCountOverflowException) { return new(application.Changed, "The script ran too long and was stopped.", alerts, output); }
        catch (RecursionDepthOverflowException) { return new(application.Changed, "The script recursed too deeply and was stopped.", alerts, output); }
        catch (ExecutionCanceledException) { return new(application.Changed, "The script was canceled.", alerts, output); }
        catch (Acornima.ParseErrorException error) { return new(false, $"The script could not be read: {error.Description} (line {error.LineNumber})", alerts, output); }
    }

    internal static string Text(object? value) => value switch
    {
        null => "undefined",
        double d => d.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "",
    };
}

/// <summary>A script's own error, which reaches it as a JavaScript exception.</summary>
public sealed class ScriptException(string message) : Exception(message);

public sealed class ScriptDollar(List<string> output)
{
    public void Writeln(object? value) { if (output.Count < 1000) output.Add(ScriptHost.Text(value)); }
    public void Write(object? value) => Writeln(value);
}

public sealed class ScriptApplication(CanvasDocument? document, Guid? activeLayer, string documentName)
{
    private ScriptDocument? _active;
    internal bool Changed => _active?.Changed == true;
    public string Name => "Compositor";
    public string Version => typeof(ScriptHost).Assembly.GetName().Version?.ToString(3) ?? "1";
    public ScriptDocument ActiveDocument => document is null ? throw new ScriptException("There is no open document.")
        : _active ??= new ScriptDocument(document, activeLayer, documentName);
    public ScriptDocument[] Documents => document is null ? [] : [ActiveDocument];
}

public sealed class ScriptDocument(CanvasDocument document, Guid? activeLayer, string name)
{
    internal bool Changed { get; private set; }
    internal CanvasDocument Model => document;
    internal void Touch() => Changed = true;
    public string Name => name;
    public double Width => document.Width;
    public double Height => document.Height;
    public double Resolution => document.Resolution;
    public string Mode => document.Image.Mode.ToString().ToUpperInvariant();
    public int BitsPerChannel => document.Image.Depth;
    public ScriptInfo Info => new(this);
    /// <summary>Top first, as Photoshop lists them.</summary>
    public ScriptLayer[] Layers => [.. document.Layers.Where(l => l.ParentID is null).Reverse().Select(l => new ScriptLayer(this, l.ID))];
    public ScriptLayer[] ArtLayers => [.. document.Layers.Where(l => !l.IsGroup).Reverse().Select(l => new ScriptLayer(this, l.ID))];
    public ScriptLayer[] LayerSets => [.. document.Layers.Where(l => l.IsGroup).Reverse().Select(l => new ScriptLayer(this, l.ID))];
    public ScriptLayer? ActiveLayer => activeLayer is { } id && document.Layers.Any(l => l.ID == id) ? new ScriptLayer(this, id)
        : document.Layers.LastOrDefault() is { } top ? new ScriptLayer(this, top.ID) : null;

    public void ResizeImage(double width, double height, double? resolution = null)
    {
        var w = (int)Math.Round(width); var h = (int)Math.Round(height);
        if (!double.IsFinite(width) || !double.IsFinite(height) || w < 1 || h < 1) throw new ScriptException("resizeImage needs a width and height in pixels.");
        if (ImageEdits.Resize(document, w, h, resolution is { } r && double.IsFinite(r) && r > 0 ? r : document.Resolution)) Changed = true;
        else throw new ScriptException("The image cannot be that size.");
    }
    public void RotateCanvas(double angle)
    {
        if (!double.IsFinite(angle)) throw new ScriptException("rotateCanvas needs an angle.");
        if (ImageGeometry.Rotate(document, angle)) Changed = true;
    }
    public void Flatten() { if (LayerWorkflow.MergeVisible(document, true) is not null) Changed = true; }
    public void MergeVisibleLayers() { if (LayerWorkflow.MergeVisible(document, false) is not null) Changed = true; }
}

public sealed class ScriptInfo(ScriptDocument document)
{
    private Format.DocumentMetadata Data => document.Model.Metadata;
    private void Set(Func<Format.DocumentMetadata, Format.DocumentMetadata> change)
    {
        var updated = change(Data);
        if (updated == Data) return;
        document.Model.Metadata = updated; document.Touch();
    }
    private static string Clean(string? value) => (value ?? "").Length > 2000 ? value![..2000] : value ?? "";
    public string Author { get => Data.Author; set => Set(m => m with { Author = Clean(value) }); }
    public string Title { get => Data.Title; set => Set(m => m with { Title = Clean(value) }); }
    public string Caption { get => Data.Description; set => Set(m => m with { Description = Clean(value) }); }
    public string CopyrightNotice { get => Data.Copyright; set => Set(m => m with { Copyright = Clean(value) }); }
    public string Keywords { get => Data.Keywords; set => Set(m => m with { Keywords = Clean(value) }); }
}

public sealed class ScriptLayer(ScriptDocument document, Guid id)
{
    private ImageLayer Layer => document.Model.Layers.FirstOrDefault(l => l.ID == id) ?? throw new ScriptException("The layer has been removed.");
    private bool Editable => LayerWorkflow.Editable(document.Model, [id]);
    private void Change(Action<ImageLayer> change)
    {
        if (!Editable) throw new ScriptException("The layer is locked.");
        change(Layer); document.Touch();
    }

    public string Name { get => Layer.Name; set => Change(l => l.Name = string.IsNullOrWhiteSpace(value) ? l.Name : value.Length > 255 ? value[..255] : value); }
    public bool Visible { get => Layer.IsVisible; set => Change(l => l.IsVisible = value); }
    /// <summary>0–100, as Photoshop's scripting has it.</summary>
    public double Opacity
    {
        get => Math.Round(Layer.Opacity * 100, 2);
        set => Change(l => l.Opacity = double.IsFinite(value) ? Math.Clamp(value, 0, 100) / 100 : throw new ScriptException("Opacity is 0 to 100."));
    }
    public string BlendMode
    {
        get => Layer.BlendMode.ToString().ToUpperInvariant();
        set => Change(l => l.BlendMode = (value ?? "").Replace("BlendMode.", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant() switch
        {
            "COLORBLEND" => Format.LayerBlendMode.Color,
            var name => Enum.GetValues<Format.LayerBlendMode>().FirstOrDefault(m => m.ToString().Equals(name, StringComparison.OrdinalIgnoreCase), (Format.LayerBlendMode)(-1)) is var mode && Enum.IsDefined(mode)
                ? mode : throw new ScriptException($"Unknown blend mode {value}."),
        });
    }
    public string Typename => Layer.IsGroup ? "LayerSet" : "ArtLayer";
    public bool IsBackgroundLayer => false;
    /// <summary>Left, top, right and bottom in pixels.</summary>
    public double[] Bounds
    {
        get
        {
            var box = LayerWorkflow.Bounds(document.Model, [id]);
            return box.IsEmpty ? [0, 0, 0, 0] : [box.Left, box.Top, box.Right, box.Bottom];
        }
    }
    public ScriptLayer[] Layers => [.. document.Model.Layers.Where(l => l.ParentID == id).Reverse().Select(l => new ScriptLayer(document, l.ID))];

    public void Translate(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ScriptException("translate needs two distances in pixels.");
        if (!LayerProtection.CanMove(document.Model, id)) throw new ScriptException("The layer cannot be moved.");
        if (dx == 0 && dy == 0) return;
        // The layer and, for a group, everything in it; a linked mask that sits apart moves along.
        foreach (var moved in document.Model.Layers.Where(l => l.ID == id || document.Model.Descendants(id).Contains(l.ID)))
        {
            moved.Transform = moved.Transform with { X = moved.Transform.X + dx, Y = moved.Transform.Y + dy };
            if (moved.Mask is { IsLinked: true, Placement: { } placement }) moved.Mask.Placement = placement with { X = placement.X + dx, Y = placement.Y + dy };
        }
        document.Touch();
    }
    public void Remove()
    {
        if (!Editable) throw new ScriptException("The layer is locked.");
        if (LayerEdits.Delete(document.Model, id)) document.Touch();
    }
}
