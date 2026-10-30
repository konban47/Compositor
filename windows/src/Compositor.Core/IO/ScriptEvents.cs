using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Rendering;

namespace Compositor.Core.IO;

/// <summary>
/// The document events the Script Events Manager can attach work to. <see cref="Command"/> is an event added with
/// "Add Event…": a named menu command of this app, matched by its stable English command name.
/// </summary>
public enum ScriptEvent
{
    StartApplication, NewDocument, OpenDocument, StartSaveDocument, SaveDocument, CloseDocument,
    PrintDocument, ExportDocument, Everything, Command,
}

public enum ScriptEventTarget { Script, Action }

/// <summary>
/// Scripts shipped with the app: native implementations of the sample event scripts' intent. A binding can instead
/// name a JavaScript or ExtendScript file of the user's, which <see cref="ScriptHost"/> runs.
/// </summary>
public enum EventScript { CleanListener, DisplayCameraMaker, OpenAsLayer, Resize, SaveExtraJpeg, UpdateFileInfo, WarnIfRgb, Welcome }

public sealed record ScriptEventBinding(
    ScriptEvent Event,
    ScriptEventTarget Target,
    EventScript Script = EventScript.Welcome,
    BatchAction Action = BatchAction.AutoColor,
    string Command = "",
    string ScriptFile = "")
{
    /// <summary>The one action set the manager offers: the same built-in adjustments Batch and Image Processor run.</summary>
    [JsonIgnore] public string ActionSet => ScriptEvents.BasicActionSet;

    public bool Matches(ScriptEvent happened, string? command = null)
    {
        if (happened == ScriptEvent.Command)
            return Event == ScriptEvent.Command && command is { Length: > 0 } && string.Equals(Command, command, StringComparison.Ordinal);
        if (Event == ScriptEvent.Everything) return happened is not (ScriptEvent.Everything or ScriptEvent.Command);
        return Event == happened;
    }

    /// <summary>Equivalent rows are one association: the manager never runs the same work twice for one event.</summary>
    public bool SameAs(ScriptEventBinding other) => Event == other.Event && Target == other.Target
        && (Event != ScriptEvent.Command || string.Equals(Command, other.Command, StringComparison.Ordinal))
        && (Target == ScriptEventTarget.Script ? Script == other.Script && string.Equals(ScriptFile, other.ScriptFile, StringComparison.OrdinalIgnoreCase) : Action == other.Action);
}

public sealed record ScriptEventSettings(int Version, bool Enabled, IReadOnlyList<ScriptEventBinding> Bindings, IReadOnlyList<string> CustomEvents)
{
    public static ScriptEventSettings Default { get; } = new(1, true, [], []);
}

public sealed record ScriptEventRun(ScriptEventBinding Binding, string? Error);

/// <summary>Validation, persistence and the activity log behind File ▸ Scripts ▸ Script Events Manager.</summary>
public static class ScriptEvents
{
    public const string BasicActionSet = "Basic Adjustments";
    public const int MaximumBindings = 100;
    public const int MaximumCustomEvents = 50;
    public const int MaximumCommandLength = 200;
    public const long MaximumLogBytes = 1024 * 1024;

    public static IReadOnlyList<ScriptEvent> DocumentEvents { get; } =
    [
        ScriptEvent.StartApplication, ScriptEvent.NewDocument, ScriptEvent.OpenDocument, ScriptEvent.StartSaveDocument,
        ScriptEvent.SaveDocument, ScriptEvent.CloseDocument, ScriptEvent.PrintDocument, ScriptEvent.ExportDocument, ScriptEvent.Everything,
    ];

    public static IReadOnlyList<EventScript> Scripts { get; } = Enum.GetValues<EventScript>();
    public static IReadOnlyList<BatchAction> Actions { get; } = Enum.GetValues<BatchAction>().Where(a => a != BatchAction.None).ToArray();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string SettingsPath => Path.Combine(AppPaths.SettingsDirectory, "script-events.json");
    public static string LogPath => Path.Combine(AppPaths.SettingsDirectory, "script-events.log");

    /// <summary>English names; the desktop translates them at its boundary.</summary>
    public static string EventName(ScriptEvent value) => value switch
    {
        ScriptEvent.StartApplication => "Start Application",
        ScriptEvent.NewDocument => "New Document",
        ScriptEvent.OpenDocument => "Open Document",
        ScriptEvent.StartSaveDocument => "Start Save Document",
        ScriptEvent.SaveDocument => "Save Document",
        ScriptEvent.CloseDocument => "Close Document",
        ScriptEvent.PrintDocument => "Print Document",
        ScriptEvent.ExportDocument => "Export Document",
        ScriptEvent.Everything => "Everything",
        _ => "Command",
    };

    public static string ScriptName(EventScript value) => value switch
    {
        EventScript.CleanListener => "Clean Listener",
        EventScript.DisplayCameraMaker => "Display Camera Maker",
        EventScript.OpenAsLayer => "Open As Layer",
        EventScript.Resize => "Resize",
        EventScript.SaveExtraJpeg => "Save Extra JPEG",
        EventScript.UpdateFileInfo => "Update File Info",
        EventScript.WarnIfRgb => "Warn if RGB",
        _ => "Welcome",
    };

    public static string ScriptDescription(EventScript value) => value switch
    {
        EventScript.CleanListener => "Deletes the Script Events activity log, so the next events start a fresh record.",
        EventScript.DisplayCameraMaker => "Shows the camera maker and model recorded in File Info for the document.",
        EventScript.OpenAsLayer => "When a document opens while another is open, places the new document's composite into the previous document as a layer and closes the new tab.",
        EventScript.Resize => "Fits the document within 1920 × 1080 pixels without enlarging it, as one undoable step.",
        EventScript.SaveExtraJpeg => "After the project is saved, writes a JPEG copy beside the .comp folder with the same name.",
        EventScript.UpdateFileInfo => "Before saving, fills an empty File Info author with the Windows user name and an empty copyright with the year and author.",
        EventScript.WarnIfRgb => "Warns when the document's color mode is RGB.",
        _ => "Shows a welcome message.",
    };

    public static string ActionName(BatchAction value) => value switch
    {
        BatchAction.AutoColor => "Auto Color",
        BatchAction.AutoTone => "Auto Tone",
        BatchAction.AutoContrast => "Auto Contrast",
        BatchAction.Vibrance => "Vibrance",
        BatchAction.Desaturate => "Desaturate",
        BatchAction.Equalize => "Equalize",
        _ => "None",
    };

    /// <summary>Whether running this binding changes pixels or File Info, so it needs the active document and a history step.</summary>
    public static bool ChangesDocument(ScriptEventBinding binding) => binding.Target == ScriptEventTarget.Action
        || binding.Script is EventScript.Resize or EventScript.UpdateFileInfo or EventScript.OpenAsLayer || binding.ScriptFile.Length > 0;

    public static void Validate(ScriptEventSettings settings)
    {
        if (settings is null || settings.Version != 1 || settings.Bindings is null || settings.CustomEvents is null)
            throw new InvalidDataException("Invalid script events settings.");
        if (settings.Bindings.Count > MaximumBindings) throw new InvalidDataException($"At most {MaximumBindings} event associations are supported.");
        if (settings.CustomEvents.Count > MaximumCustomEvents) throw new InvalidDataException($"At most {MaximumCustomEvents} added events are supported.");
        foreach (var name in settings.CustomEvents) ValidateCommand(name);
        if (settings.CustomEvents.Distinct(StringComparer.Ordinal).Count() != settings.CustomEvents.Count)
            throw new InvalidDataException("Added events must be unique.");
        for (var index = 0; index < settings.Bindings.Count; index++)
        {
            var binding = settings.Bindings[index] ?? throw new InvalidDataException("Invalid event association.");
            if (!Enum.IsDefined(binding.Event) || !Enum.IsDefined(binding.Target) || !Enum.IsDefined(binding.Script) || !Enum.IsDefined(binding.Action)
                || binding.Command is null)
                throw new InvalidDataException("Invalid event association.");
            if (binding.Target == ScriptEventTarget.Action && binding.Action == BatchAction.None)
                throw new InvalidDataException("Choose an action.");
            if (binding.ScriptFile is null || binding.ScriptFile.Length > 0 && (binding.Target != ScriptEventTarget.Script || binding.ScriptFile.Length > 1024
                || !Path.IsPathRooted(binding.ScriptFile) || binding.ScriptFile.Any(char.IsControl)
                || Path.GetExtension(binding.ScriptFile).ToLowerInvariant() is not (".js" or ".jsx")))
                throw new InvalidDataException("Choose a JavaScript (.js or .jsx) file.");
            if (binding.Event == ScriptEvent.Command) ValidateCommand(binding.Command);
            else if (binding.Command.Length != 0) throw new InvalidDataException("Only added events name a command.");
            for (var other = 0; other < index; other++)
                if (settings.Bindings[other].SameAs(binding)) throw new InvalidDataException("That script or action is already associated with this event.");
        }
    }

    private static void ValidateCommand(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumCommandLength || name.Any(char.IsControl))
            throw new InvalidDataException("Invalid event name.");
    }

    /// <summary>Adds a row to an in-memory copy; the original is untouched so the dialog can still be canceled.</summary>
    public static ScriptEventSettings Add(ScriptEventSettings settings, ScriptEventBinding binding)
    {
        var bindings = settings.Bindings.Append(binding).ToArray();
        var custom = settings.CustomEvents;
        if (binding.Event == ScriptEvent.Command && !custom.Contains(binding.Command, StringComparer.Ordinal))
            custom = custom.Append(binding.Command).ToArray();
        var result = settings with { Bindings = bindings, CustomEvents = custom };
        Validate(result);
        return result;
    }

    public static ScriptEventSettings RemoveAt(ScriptEventSettings settings, IEnumerable<int> indexes)
    {
        var removed = indexes.ToHashSet();
        return settings with { Bindings = settings.Bindings.Where((_, index) => !removed.Contains(index)).ToArray() };
    }

    public static IReadOnlyList<ScriptEventBinding> Matching(ScriptEventSettings settings, ScriptEvent happened, string? command = null) =>
        settings.Enabled ? settings.Bindings.Where(binding => binding.Matches(happened, command)).ToArray() : [];

    public static ScriptEventSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        if (!File.Exists(path)) return ScriptEventSettings.Default;
        using var stream = File.OpenRead(path);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Script events settings file is too large.");
        var settings = JsonSerializer.Deserialize<ScriptEventSettings>(stream, Json) ?? throw new InvalidDataException("Invalid script events settings.");
        settings = settings with { Bindings = settings.Bindings ?? [], CustomEvents = settings.CustomEvents ?? [] };
        Validate(settings);
        return settings;
    }

    public static void Save(ScriptEventSettings settings, string? path = null)
    {
        Validate(settings);
        path ??= SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(settings, Json));
    }

    /// <summary>One line an event run; the oldest half is dropped once the log passes 1 MB.</summary>
    public static void AppendLog(string line, DateTime? at = null, string? path = null)
    {
        path ??= LogPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var text = (at ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\t"
            + line.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine;
        if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(text) > MaximumLogBytes)
        {
            var lines = File.ReadAllLines(path);
            File.WriteAllLines(path, lines.Skip(lines.Length / 2));
        }
        File.AppendAllText(path, text, Encoding.UTF8);
    }

    /// <summary>Clean Listener: true when there was a log to delete.</summary>
    public static bool CleanLog(string? path = null)
    {
        path ??= LogPath;
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public static string Describe(ScriptEventBinding binding) =>
        (binding.Event == ScriptEvent.Command ? binding.Command : EventName(binding.Event)) + ": "
        + (binding.Target == ScriptEventTarget.Script ? ScriptName(binding.Script) : BasicActionSet + " ▸ " + ActionName(binding.Action));
}

/// <summary>The document work of the built-in event scripts, kept here so it is testable without a window.</summary>
public static class EventScriptWork
{
    public const int ResizeWidth = 1920;
    public const int ResizeHeight = 1080;

    public static bool Resize(CanvasDocument document)
    {
        var size = FileAutomation.Fit(document.Width, document.Height, ResizeWidth, ResizeHeight, true);
        return (size.Width, size.Height) != (document.Width, document.Height)
            && ImageEdits.Resize(document, size.Width, size.Height, document.Resolution, LayerSampling.HighQuality);
    }

    public static DocumentMetadata UpdateFileInfo(DocumentMetadata metadata, string user, DateTime now)
    {
        var author = metadata.Author.Length > 0 ? metadata.Author : user.Trim();
        var copyright = metadata.Copyright.Length > 0 || author.Length == 0
            ? metadata.Copyright
            : "© " + now.Year.ToString(CultureInfo.InvariantCulture) + " " + author;
        var result = metadata with { Author = author, Copyright = copyright };
        return result.IsValid ? result : metadata;
    }

    public static string CameraMaker(CanvasDocument document) =>
        string.Join(" ", new[] { document.Metadata.CameraMake, document.Metadata.CameraModel }.Where(s => s.Length > 0));

    public static bool IsRgb(CanvasDocument document) => document.Image.Mode == ImageMode.RGB;

    /// <summary>The JPEG written beside a saved .comp folder: Name.comp → Name.jpg in the same parent.</summary>
    public static string ExtraJpegPath(string projectPath)
    {
        var full = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileNameWithoutExtension(full);
        return Path.Combine(Path.GetDirectoryName(full)!, (name.Length == 0 ? "Untitled" : name) + ".jpg");
    }

    public static string SaveExtraJpeg(CanvasDocument document, string projectPath, CancellationToken cancellation = default)
    {
        var path = ExtraJpegPath(projectPath);
        using var lease = new DocumentLease(document);
        var copy = lease.Document;
        using var encoded = ExportImage.Encode(copy, new ExportOptions(ExportFormat.Jpeg, copy.Width, copy.Height, 85,
            Resolution: copy.Resolution, Metadata: MetadataPolicy.CopyrightAndContact));
        AtomicFile.WriteBytes(path, encoded.Bytes, cancellation);
        return path;
    }

    /// <summary>Open As Layer: the opened document flattened into one centered layer of the target, which keeps its own size.</summary>
    public static ImageLayer CompositeLayer(CanvasDocument source, CanvasDocument target, string name)
    {
        var pixels = target.Layers.Sum(layer => (long)(layer.Asset?.Width ?? 0) * (layer.Asset?.Height ?? 0));
        if (pixels + (long)source.Width * source.Height > DocumentLimits.DocumentPixelBudget)
            throw new InvalidOperationException("The opened document is too large to add as a layer.");
        var image = ImportedImage.Create(DocumentRenderer.Render(source), name);
        var placement = new Model.LayerTransform((target.Width - image.Width) / 2.0, (target.Height - image.Height) / 2.0, image.Width, image.Height);
        return new ImageLayer(Guid.NewGuid(), image, placement, name);
    }
}
