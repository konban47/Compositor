using Avalonia.Controls;
using Avalonia.LogicalTree;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

/// <summary>
/// File ▸ Scripts ▸ Script Events Manager: built-in scripts and Basic Adjustments actions run when documents are
/// created, opened, saved, closed or exported, or when an added menu command is chosen. Document changes go
/// through <see cref="Edit"/>, so each one is a single undoable history step.
/// </summary>
public partial class MainWindow
{
    private static bool _startApplicationRaised;
    private ScriptEventSettings? _scriptEventSettings;
    private int _scriptEventDepth;
    /// <summary>How script messages are shown; the self check replaces it so nothing waits for a click.</summary>
    internal Func<string, string, Task>? ScriptEventNotice { get; set; }
    /// <summary>Lines of the most recent dispatches, newest last, for the self check and the status line.</summary>
    internal List<string> ScriptEventReport { get; } = [];

    private ScriptEventSettings EventSettings
    {
        get
        {
            if (_scriptEventSettings is not null) return _scriptEventSettings;
            try { _scriptEventSettings = ScriptEvents.Load(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                _scriptEventSettings = ScriptEventSettings.Default;
                Say($"Script events settings could not be read: {error.Message}");
            }
            return _scriptEventSettings;
        }
    }

    private async Task ShowScriptEvents()
    {
        FinishPlacement(true); FinishPen(); CommitText();
        var result = await ScriptEventsDialog.Ask(this, EventSettings, MenuCommands());
        if (result is null) return;
        try
        {
            ScriptEvents.Save(result);
            _scriptEventSettings = result;
            Say(result.Bindings.Count == 0 ? "No scripts/actions associated with events" : $"{result.Bindings.Count} event association(s) saved");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { Say($"Could not save script events: {error.Message}"); }
    }

    /// <summary>Every leaf command of the menu bar, by its stable English name: what "Add Event…" can listen for.</summary>
    internal IReadOnlyList<string> MenuCommands()
    {
        var names = new List<string>();
        void Walk(IEnumerable<object?> items)
        {
            foreach (var item in items.OfType<MenuItem>())
            {
                var children = item.Items.Cast<object?>().ToArray();
                if (children.Length > 0) Walk(children);
                else if (item.Tag is string name && name.Length is > 0 and <= ScriptEvents.MaximumCommandLength && !names.Contains(name)) names.Add(name);
            }
        }
        if (this.GetLogicalDescendants().OfType<Menu>().FirstOrDefault() is { } menu) Walk(menu.Items.Cast<object?>());
        return names;
    }

    private void RaiseStartApplication()
    {
        if (_startApplicationRaised) return;
        _startApplicationRaised = true;
        _ = RaiseScriptEvent(ScriptEvent.StartApplication, null);
    }

    private void RaiseCommandEvent(string command)
    {
        if (_scriptEventDepth > 0 || EventSettings is not { Enabled: true } settings
            || !settings.Bindings.Any(b => b.Event == ScriptEvent.Command && b.Command == command)) return;
        _ = RaiseScriptEvent(ScriptEvent.Command, _open, command: command);
    }

    /// <summary>
    /// Runs the associations of one event in list order. Events raised while one runs — a tab closed by Open As
    /// Layer, say — do not dispatch again, so an association cannot call itself.
    /// </summary>
    private async Task RaiseScriptEvent(ScriptEvent happened, Tab? tab, Tab? previous = null, string? command = null)
    {
        if (_scriptEventDepth > 0) return;
        var bindings = ScriptEvents.Matching(EventSettings, happened, command);
        if (bindings.Count == 0) return;
        _scriptEventDepth++;
        try
        {
            foreach (var binding in bindings)
            {
                string? error;
                try { error = await RunScriptEvent(binding, happened, tab, previous); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { error = exception.Message; }
                // An "Everything" association is quietly left out of events it has nothing to do in.
                if (error == NotApplicable) continue;
                var line = ScriptEvents.Describe(binding) + " — " + (happened == ScriptEvent.Command ? command : ScriptEvents.EventName(happened))
                    + (error is null ? " — OK" : " — " + error);
                ScriptEventReport.Add(line);
                if (ScriptEventReport.Count > 200) ScriptEventReport.RemoveAt(0);
                if (binding is not { Target: ScriptEventTarget.Script, Script: EventScript.CleanListener } || error is not null)
                    try { ScriptEvents.AppendLog(line); } catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
                if (error is not null) Say($"{Localize.Text(ScriptEventsDialog.Row(binding))}: {Localize.Text(error)}");
            }
        }
        finally { _scriptEventDepth--; }
    }

    private const string NotApplicable = "\0not-applicable";

    private Task Notice(string title, string message) =>
        ScriptEventNotice is { } notice ? notice(title, message) : ConfirmDialog.Tell(this, title, message);

    /// <summary>One association; null when it ran, otherwise why it was skipped or failed.</summary>
    private async Task<string?> RunScriptEvent(ScriptEventBinding binding, ScriptEvent happened, Tab? tab, Tab? previous)
    {
        string? Skip(string reason) => binding.Event == ScriptEvent.Everything ? NotApplicable : reason;
        // Pixel and File Info changes need the document the event is about to be in front, with its own history.
        var document = tab?.Document;
        if (ScriptEvents.ChangesDocument(binding) && (document is null || !ReferenceEquals(tab, _open) || happened == ScriptEvent.CloseDocument))
            return Skip("This script or action needs the event's document to be open in front.");
        if (binding.Target == ScriptEventTarget.Action)
        {
            if (document is null) return Skip("No document for this event.");
            if (Selected is not { } id || document.Layers.FirstOrDefault(layer => layer.ID == id) is not { Asset: not null, IsGroup: false, Adjustment: null })
                return "Select a layer with pixels for this action.";
            var acting = document;
            var changed = Edit(ScriptEvents.ActionName(binding.Action), () => BatchProcessing.ApplyTo(acting, id, binding.Action));
            Reselect(id);
            return changed ? null : "The action did not change the layer.";
        }
        switch (binding.Script)
        {
            case EventScript.CleanListener:
                ScriptEvents.CleanLog();
                Say("Script events log cleared");
                return null;
            case EventScript.DisplayCameraMaker:
                if (document is null) return Skip("No document for this event.");
                var maker = EventScriptWork.CameraMaker(document);
                await Notice("Display Camera Maker", maker.Length > 0
                    ? Localize.Text("Camera maker") + ": " + maker
                    : Localize.Text("No camera maker is recorded in File Info."));
                return null;
            case EventScript.OpenAsLayer:
                if (happened != ScriptEvent.OpenDocument || tab is null) return Skip("Open As Layer runs when a document is opened.");
                return await OpenAsLayer(tab, previous);
            case EventScript.Resize:
                if (document is null) return Skip("No document for this event.");
                var resizing = document;
                return Edit("Resize", () => EventScriptWork.Resize(resizing)) ? null : "The document already fits.";
            case EventScript.SaveExtraJpeg:
                if (document is null || tab?.Path is not { } path || happened != ScriptEvent.SaveDocument)
                    return Skip("Save Extra JPEG runs after a project is saved.");
                using (var lease = new DocumentLease(document))
                {
                    var copy = lease.Document;
                    var written = await Task.Run(() => EventScriptWork.SaveExtraJpeg(copy, path));
                    Say($"Saved {written}");
                }
                return null;
            case EventScript.UpdateFileInfo:
                if (document is null) return Skip("No document for this event.");
                var described = document;
                var updated = EventScriptWork.UpdateFileInfo(described.Metadata, Environment.UserName, DateTime.Now);
                if (updated == described.Metadata) return null;
                return Edit("Update File Info", () => { described.Metadata = updated; return true; }) ? null : "File Info was not changed.";
            case EventScript.WarnIfRgb:
                if (document is not null && EventScriptWork.IsRgb(document))
                    await Notice("Warn if RGB", Localize.Text("This document is in RGB color mode."));
                return null;
            default:
                await Notice("Welcome", Localize.Text("Welcome to Compositor."));
                return null;
        }
    }

    /// <summary>
    /// Open As Layer: the document just opened goes into the one that was in front before it, as a layer of its
    /// composite, and its own tab closes. One undo step in the receiving document takes the layer out again.
    /// </summary>
    private async Task<string?> OpenAsLayer(Tab opened, Tab? previous)
    {
        if (previous is null || ReferenceEquals(previous, opened) || !_tabs.Contains(previous) || previous.Document is not { } target || opened.Document is not { } source)
            return "Open As Layer needs another document open to receive the layer.";
        var layer = EventScriptWork.CompositeLayer(source, target, opened.Name);
        Bring(previous);
        // A failed edit rolls the document back and releases the new layer's pixels with it.
        if (!Edit("Open As Layer", () => { target.Layers.Add(layer); return true; })) return "The layer could not be added.";
        Reselect(layer.ID);
        await CloseTab(opened, true);
        Bring(previous);
        return null;
    }
}
