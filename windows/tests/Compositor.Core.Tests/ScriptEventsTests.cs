using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public sealed class ScriptEventsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "compositor-script-events-" + Guid.NewGuid().ToString("N"));

    public ScriptEventsTests() => Directory.CreateDirectory(_directory);
    public void Dispose() { try { Directory.Delete(_directory, true); } catch (IOException) { } }

    private static CanvasDocument Source(int width, int height, SKColor color)
    {
        var pixels = new SKBitmap(width, height);
        pixels.Erase(color);
        return ImageImporter.NewDocument(ImportedImage.Create(pixels, "Example"));
    }

    private static readonly ScriptEventBinding Welcome = new(ScriptEvent.StartApplication, ScriptEventTarget.Script, EventScript.Welcome);
    private static readonly ScriptEventBinding SaveJpeg = new(ScriptEvent.SaveDocument, ScriptEventTarget.Script, EventScript.SaveExtraJpeg);
    private static readonly ScriptEventBinding OpenAction = new(ScriptEvent.OpenDocument, ScriptEventTarget.Action, Action: BatchAction.Desaturate);
    private static readonly ScriptEventBinding CommandScript = new(ScriptEvent.Command, ScriptEventTarget.Script, EventScript.WarnIfRgb, Command: "Auto Tone");

    [Fact]
    public void SettingsRoundTripWithReadableNamesAndStayDisabledWhenSaved()
    {
        var path = Path.Combine(_directory, "script-events.json");
        var settings = ScriptEventSettings.Default;
        foreach (var binding in new[] { Welcome, SaveJpeg, OpenAction, CommandScript }) settings = ScriptEvents.Add(settings, binding);
        settings = settings with { Enabled = false };
        ScriptEvents.Save(settings, path);

        var text = File.ReadAllText(path);
        Assert.Contains("\"SaveExtraJpeg\"", text);
        Assert.Contains("\"Desaturate\"", text);
        var loaded = ScriptEvents.Load(path);
        Assert.False(loaded.Enabled);
        Assert.Equal(settings.Bindings, loaded.Bindings);
        Assert.Equal(["Auto Tone"], loaded.CustomEvents);
        Assert.Empty(ScriptEvents.Matching(loaded, ScriptEvent.StartApplication));
    }

    [Fact]
    public void MissingSettingsAreTheEnabledEmptyDefaultAndDamagedSettingsAreRefused()
    {
        Assert.Equal(ScriptEventSettings.Default, ScriptEvents.Load(Path.Combine(_directory, "missing.json")));
        var damaged = Path.Combine(_directory, "damaged.json");
        File.WriteAllText(damaged, "{\"Version\":2,\"Enabled\":true,\"Bindings\":[],\"CustomEvents\":[]}");
        Assert.Throws<InvalidDataException>(() => ScriptEvents.Load(damaged));
        File.WriteAllText(damaged, "{\"Version\":1,\"Enabled\":true,\"Bindings\":[{\"Event\":\"Command\",\"Target\":\"Script\",\"Command\":\"\"}],\"CustomEvents\":[]}");
        Assert.Throws<InvalidDataException>(() => ScriptEvents.Load(damaged));
    }

    [Fact]
    public void AddingWorksOnACopyAndRefusesDuplicatesAndEmptyActions()
    {
        var original = ScriptEventSettings.Default;
        var added = ScriptEvents.Add(original, Welcome);
        Assert.Empty(original.Bindings);
        Assert.Single(added.Bindings);
        Assert.Throws<InvalidDataException>(() => ScriptEvents.Add(added, Welcome with { }));
        Assert.Throws<InvalidDataException>(() => ScriptEvents.Add(added, OpenAction with { Action = BatchAction.None }));
        Assert.Throws<InvalidDataException>(() => ScriptEvents.Add(added, Welcome with { Command = "Save" }));
        // The same script on another event is a different association.
        Assert.Equal(2, ScriptEvents.Add(added, Welcome with { Event = ScriptEvent.NewDocument }).Bindings.Count);
        var full = original;
        for (var index = 0; index < ScriptEvents.MaximumBindings; index++)
            full = full with { Bindings = full.Bindings.Append(CommandScript with { Command = "Command " + index }).ToArray(),
                CustomEvents = full.CustomEvents.Count < ScriptEvents.MaximumCustomEvents ? full.CustomEvents.Append("Command " + index).ToArray() : full.CustomEvents };
        ScriptEvents.Validate(full);
        Assert.Throws<InvalidDataException>(() => ScriptEvents.Validate(full with { Bindings = full.Bindings.Append(Welcome).ToArray() }));
    }

    [Fact]
    public void RemovingKeepsTheOtherRowsInOrder()
    {
        var settings = ScriptEventSettings.Default;
        foreach (var binding in new[] { Welcome, SaveJpeg, OpenAction }) settings = ScriptEvents.Add(settings, binding);
        Assert.Equal([Welcome, OpenAction], ScriptEvents.RemoveAt(settings, [1]).Bindings);
        Assert.Empty(ScriptEvents.RemoveAt(settings, [0, 1, 2]).Bindings);
    }

    [Fact]
    public void EverythingMatchesDocumentEventsButNotMenuCommands()
    {
        var everything = new ScriptEventBinding(ScriptEvent.Everything, ScriptEventTarget.Script, EventScript.CleanListener);
        var settings = ScriptEvents.Add(ScriptEvents.Add(ScriptEventSettings.Default, everything), CommandScript);
        foreach (var happened in ScriptEvents.DocumentEvents.Where(e => e != ScriptEvent.Everything))
            Assert.Equal([everything], ScriptEvents.Matching(settings, happened));
        Assert.Equal([CommandScript], ScriptEvents.Matching(settings, ScriptEvent.Command, "Auto Tone"));
        Assert.Empty(ScriptEvents.Matching(settings, ScriptEvent.Command, "Auto Color"));
        Assert.Empty(ScriptEvents.Matching(settings, ScriptEvent.Command));
    }

    [Fact]
    public void ActivityLogAppendsTrimsAndIsCleaned()
    {
        var log = Path.Combine(_directory, "events.log");
        Assert.False(ScriptEvents.CleanLog(log));
        ScriptEvents.AppendLog("first\nline", new DateTime(2026, 10, 29, 9, 30, 0), log);
        Assert.Equal("2026-10-29 09:30:00\tfirst line", File.ReadAllLines(log).Single());
        var big = new string('x', 2000);
        for (var index = 0; index < 700; index++) ScriptEvents.AppendLog(big, path: log);
        Assert.True(new FileInfo(log).Length <= ScriptEvents.MaximumLogBytes);
        Assert.True(ScriptEvents.CleanLog(log));
        Assert.False(File.Exists(log));
    }

    [Fact]
    public void ResizeFitsWithoutEnlarging()
    {
        using var large = Source(4000, 2000, SKColors.Red);
        Assert.True(EventScriptWork.Resize(large));
        Assert.Equal((1920, 960), (large.Width, large.Height));
        using var small = Source(40, 20, SKColors.Red);
        Assert.False(EventScriptWork.Resize(small));
        Assert.Equal((40, 20), (small.Width, small.Height));
    }

    [Fact]
    public void UpdateFileInfoOnlyFillsEmptyFields()
    {
        var now = new DateTime(2026, 10, 29);
        var filled = EventScriptWork.UpdateFileInfo(new DocumentMetadata(), "Zhang", now);
        Assert.Equal("Zhang", filled.Author);
        Assert.Equal("© 2026 Zhang", filled.Copyright);
        var kept = new DocumentMetadata { Author = "Ann", Copyright = "CC BY" };
        Assert.Equal(kept, EventScriptWork.UpdateFileInfo(kept, "Zhang", now));
        Assert.Equal(new DocumentMetadata(), EventScriptWork.UpdateFileInfo(new DocumentMetadata(), " ", now));
    }

    [Fact]
    public void ExtraJpegIsWrittenBesideTheProjectFolder()
    {
        using var document = Source(30, 20, SKColors.Blue);
        var project = Path.Combine(_directory, "Poster.comp");
        Directory.CreateDirectory(project);
        Assert.Equal(Path.Combine(_directory, "Poster.jpg"), EventScriptWork.ExtraJpegPath(project + Path.DirectorySeparatorChar));
        var written = EventScriptWork.SaveExtraJpeg(document, project);
        using var decoded = SKBitmap.Decode(written);
        Assert.Equal((30, 20), (decoded.Width, decoded.Height));
        Assert.True(decoded.GetPixel(15, 10).Blue > 200);
        // Saving again replaces the same companion file instead of adding numbered copies.
        Assert.Equal(written, EventScriptWork.SaveExtraJpeg(document, project));
        Assert.Single(Directory.GetFiles(_directory, "*.jpg"));
    }

    [Fact]
    public void OpenAsLayerCentersTheOpenedComposite()
    {
        using var target = Source(100, 80, SKColors.White);
        using var opened = Source(40, 20, SKColors.Green);
        var layer = EventScriptWork.CompositeLayer(opened, target, "Opened");
        try
        {
            Assert.Equal(new LayerTransform(30, 30, 40, 20), layer.Transform);
            Assert.Equal("Opened", layer.Name);
            Assert.Equal(SKColors.Green, layer.Asset!.Image.GetPixel(10, 10));
        }
        finally { layer.Asset?.Dispose(); }
    }

    [Fact]
    public void ActionsRunOnTheChosenLayer()
    {
        using var document = Source(4, 4, SKColors.Red);
        var id = document.Layers[0].ID;
        Assert.True(BatchProcessing.ApplyTo(document, id, BatchAction.Desaturate));
        var pixel = document.Layers[0].Asset!.WorkingImage.GetPixel(1, 1);
        Assert.Equal(pixel.Red, pixel.Green);
        Assert.False(BatchProcessing.ApplyTo(document, id, BatchAction.None));
    }
}
