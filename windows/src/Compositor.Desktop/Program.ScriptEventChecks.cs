using Avalonia.Controls;
using Avalonia.Interactivity;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed partial class ScriptEventsDialog
{
    /// <summary>Layout, chooser state, add/remove, duplicates, Add Event, Browse and cancel/Done, without a pointer.</summary>
    internal void SelfCheck(Action pump, Action<string> capture)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        pump();
        Check(_empty.IsVisible && !_remove.IsEnabled && !_removeAll.IsEnabled, "Empty association list state is wrong.");
        Check(_event.Items.Count == ScriptEvents.DocumentEvents.Count + 2 && _eventAdd == _event.Items.Count - 1, "Event list does not end with Add Event.");
        Check(_script.Items.Count == ScriptEvents.Scripts.Count + 2 && _scriptBrowse == _script.Items.Count - 1, "Script list does not end with Browse.");
        Check(_script.IsEnabled && !_action.IsEnabled && !_actionSet.IsEnabled, "Script/action radio state is wrong.");
        Check(!string.IsNullOrWhiteSpace(_description.Text), "Script description is missing.");
        capture("script-events");

        _event.SelectedIndex = ScriptEvents.DocumentEvents.ToList().IndexOf(ScriptEvent.SaveDocument);
        _script.SelectedIndex = ScriptEvents.Scripts.ToList().IndexOf(EventScript.SaveExtraJpeg); pump();
        Check(_description.Text == Localize.Text(ScriptEvents.ScriptDescription(EventScript.SaveExtraJpeg)), "Description did not follow the script.");
        Add();
        Check(_settings.Bindings.Count == 1 && !_empty.IsVisible && _removeAll.IsEnabled, "Add did not list the association.");
        Add();
        Check(_settings.Bindings.Count == 1 && _error.Text?.Length > 0, "A duplicate association was accepted.");

        _useAction.IsChecked = true; pump();
        Check(!_script.IsEnabled && _action.IsEnabled, "Action radio did not enable the action lists.");
        _event.SelectedIndex = ScriptEvents.DocumentEvents.ToList().IndexOf(ScriptEvent.OpenDocument);
        _action.SelectedIndex = ScriptEvents.Actions.ToList().IndexOf(BatchAction.Desaturate);
        Add();
        Check(_settings.Bindings[^1] == new ScriptEventBinding(ScriptEvent.OpenDocument, ScriptEventTarget.Action, Action: BatchAction.Desaturate),
            "Action association is wrong.");

        PickCommand = commands => Task.FromResult<string?>(commands.Contains("Auto Tone") ? "Auto Tone" : commands.FirstOrDefault());
        _event.SelectedIndex = _eventAdd; pump();
        Check(_settings.CustomEvents.Count == 1 && _eventRows[_event.SelectedIndex].Event == ScriptEvent.Command, "Add Event did not select the new event.");
        _useScript.IsChecked = true; _script.SelectedIndex = ScriptEvents.Scripts.ToList().IndexOf(EventScript.Welcome);
        Add();
        Check(_settings.Bindings[^1].Event == ScriptEvent.Command && _settings.Bindings[^1].Command == _settings.CustomEvents[0], "Command association is wrong.");

        // A compiled .jsxbin is refused; a .jsx source is listed after the built-in scripts and can be associated.
        PickScriptFile = () => Task.FromResult<string?>(Path.Combine(Path.GetTempPath(), "Sample.jsxbin"));
        var before = _script.SelectedIndex;
        _script.SelectedIndex = _scriptBrowse; pump();
        Check(_script.SelectedIndex == before && _error.Text?.Contains(".jsx") == true, "A compiled .jsxbin script was accepted.");
        var sample = Path.Combine(Path.GetTempPath(), "Sample.jsx");
        PickScriptFile = () => Task.FromResult<string?>(sample);
        _script.SelectedIndex = _scriptBrowse; pump();
        Check(_script.SelectedIndex == ScriptEvents.Scripts.Count && _description.Text?.Contains("Sample.jsx") == true, "A chosen JavaScript file was not listed.");
        var count = _settings.Bindings.Count;
        Add();
        Check(_settings.Bindings.Count == count + 1 && _settings.Bindings[^1].ScriptFile == sample, "A JavaScript file was not associated.");
        capture("script-events-filled");
        _list.SelectedIndex = _settings.Bindings.Count - 1; pump(); RemoveSelected();

        _list.SelectedIndex = 0; pump(); RemoveSelected();
        Check(_settings.Bindings.Count == 2 && _settings.Bindings[0].Target == ScriptEventTarget.Action, "Remove deleted the wrong association.");
        _enabled.IsChecked = false; pump();
        Check(!_settings.Enabled, "Enable switch did not change the draft.");
        RemoveAll();
        Check(_settings.Bindings.Count == 0 && _empty.IsVisible, "Remove All left associations.");
        Close(); pump();
        Check(_result is null, "Closing the window did not cancel.");
    }

    internal void DoneForCheck() => Done();
    internal ScriptEventSettings? ResultForCheck => _result;
}

public partial class MainWindow
{
    /// <summary>Saved settings reload, then open/command/save/export/close events run with undo, a JPEG beside the save and notices.</summary>
    internal void ScriptEventSelfCheck(Action pump, Action<Window, string> capture)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Finish(Task task)
        {
            var end = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < end) { pump(); Thread.Sleep(5); }
            Check(task.IsCompleted, "Script event task timed out."); task.GetAwaiter().GetResult(); pump();
        }
        var commands = MenuCommands();
        Check(commands.Contains("Script Events Manager…") && commands.Contains("Auto Tone"), "Menu commands for Add Event are missing.");

        var dialog = new ScriptEventsDialog(ScriptEventSettings.Default, commands);
        _ = dialog.ShowDialog(this); dialog.SelfCheck(pump, suffix => capture(dialog, suffix));
        var accepted = new ScriptEventsDialog(ScriptEventSettings.Default, commands);
        _ = accepted.ShowDialog(this); pump(); accepted.Add(); accepted.DoneForCheck(); pump();
        Check(accepted.ResultForCheck is { Bindings.Count: 1 }, "Done did not return the edited settings.");

        var folder = Path.Combine(AppPaths.SettingsDirectory, "script-event-check");
        Directory.CreateDirectory(folder);
        var notices = new List<string>();
        ScriptEventNotice = (title, _) => { notices.Add(title); return Task.CompletedTask; };
        try
        {
            var settings = ScriptEventSettings.Default;
            foreach (var binding in new ScriptEventBinding[]
            {
                new(ScriptEvent.OpenDocument, ScriptEventTarget.Action, Action: BatchAction.Desaturate),
                new(ScriptEvent.Command, ScriptEventTarget.Script, EventScript.Welcome, Command: "Auto Tone"),
                new(ScriptEvent.StartSaveDocument, ScriptEventTarget.Script, EventScript.UpdateFileInfo),
                new(ScriptEvent.SaveDocument, ScriptEventTarget.Script, EventScript.SaveExtraJpeg),
                new(ScriptEvent.ExportDocument, ScriptEventTarget.Script, EventScript.WarnIfRgb),
                new(ScriptEvent.CloseDocument, ScriptEventTarget.Script, EventScript.DisplayCameraMaker),
                new(ScriptEvent.CloseDocument, ScriptEventTarget.Action, Action: BatchAction.AutoTone),
            }) settings = ScriptEvents.Add(settings, binding);
            ScriptEvents.Save(settings);
            _scriptEventSettings = null;
            Check(EventSettings.Bindings.Count == 7, "Saved script events did not reload.");

            var image = Path.Combine(folder, "Opened.png");
            using (var pixels = new SKBitmap(Bitmaps.ColorInfo(24, 16)))
            {
                pixels.Erase(SKColors.Red);
                using var data = pixels.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(image, data.ToArray());
            }
            var previous = _open;
            Finish(OpenPath(image));
            Check(!ReferenceEquals(previous, _open) && _document is not null, "The image did not open in a tab.");
            SKColor Pixel() => _document!.Layers[^1].Asset!.WorkingImage.GetPixel(4, 4);
            Check(Pixel().Red == Pixel().Green, "Open Document action did not desaturate.");
            Undo(); Check(Pixel().Red > Pixel().Green + 100, "Open Document action did not undo.");
            Redo(); Check(Pixel().Red == Pixel().Green, "Open Document action did not redo.");

            Command("Auto Tone", () => { }).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); pump();
            Check(notices.Contains("Welcome"), "Added menu command event did not run its script.");

            var project = Path.Combine(folder, "Saved.comp");
            _open.Path = project;
            Finish(SaveTab(_open, false));
            Check(File.Exists(Path.Combine(folder, "Saved.jpg")), "Save Extra JPEG did not write beside the project.");
            if (Environment.UserName.Trim().Length > 0)
            {
                Check(_document!.Metadata.Author == Environment.UserName.Trim(), "Update File Info did not run before saving.");
                using var reopened = ProjectStore.Load(project).ToDocument();
                Check(reopened.Metadata.Author == _document.Metadata.Author, "Start Save changes were not part of the saved project.");
            }

            Finish(RaiseScriptEvent(ScriptEvent.ExportDocument, _open));
            Check(notices.Contains("Warn if RGB"), "Export Document script did not run.");

            var closing = _open;
            Finish(CloseTab(closing, true));
            Check(notices.Contains("Display Camera Maker") && !_tabs.Contains(closing), "Close Document script did not run before closing.");
            Check(ScriptEventReport.Any(line => line.Contains("Auto Tone") && !line.EndsWith(" — OK", StringComparison.Ordinal)),
                "A pixel action on a closing document was not skipped.");
            Check(File.Exists(ScriptEvents.LogPath), "Script events were not logged.");

            // A project folder and its manifest share the same open path: neither may dispatch twice.
            _scriptEventSettings = ScriptEvents.Add(ScriptEventSettings.Default,
                new(ScriptEvent.OpenDocument, ScriptEventTarget.Script, EventScript.Welcome));
            foreach (var path in new[] { project, Path.Combine(project, "manifest.json") })
            {
                notices.Clear(); Finish(OpenPath(path));
                Check(notices.SequenceEqual(["Welcome"]), "Opening a project dispatched zero or multiple open events.");
                Finish(CloseTab(_open, true));
            }
            notices.Clear(); Finish(OpenAsync(project));
            Check(notices.SequenceEqual(["Welcome"]), "The project picker open path did not dispatch exactly once.");
            notices.Clear(); Finish(OpenPath(Path.Combine(folder, "missing.png")));
            Check(notices.Count == 0, "A failed open dispatched an event.");
            Finish(OpenPath(image, false));
            Check(notices.Count == 0, "Placing an image dispatched an open event.");

            _scriptEventSettings = ScriptEvents.Add(ScriptEventSettings.Default,
                new(ScriptEvent.OpenDocument, ScriptEventTarget.Script, EventScript.OpenAsLayer));
            var receiver = _open; var tabCount = _tabs.Count; var layerCount = _document!.Layers.Count;
            Finish(OpenPath(project));
            Check(ReferenceEquals(_open, receiver) && _tabs.Count == tabCount && _document!.Layers.Count == layerCount + 1,
                "Open As Layer did not transfer to the original document exactly once.");
            Undo(); Check(_document!.Layers.Count == layerCount, "Open As Layer did not undo as one step.");
            Redo(); Check(_document!.Layers.Count == layerCount + 1, "Open As Layer did not redo as one step.");

            // Independent events wait behind a notice; only events caused by that script are suppressed.
            _scriptEventSettings = ScriptEvents.Add(ScriptEventSettings.Default,
                new(ScriptEvent.Everything, ScriptEventTarget.Script, EventScript.Welcome));
            var noticeGate = new TaskCompletionSource(); var noticeCount = 0;
            ScriptEventNotice = async (_, _) =>
            {
                noticeCount++;
                if (noticeCount == 1)
                {
                    await RaiseScriptEvent(ScriptEvent.SaveDocument, _open);
                    await noticeGate.Task;
                }
            };
            var firstEvent = RaiseScriptEvent(ScriptEvent.OpenDocument, _open);
            var secondEvent = RaiseScriptEvent(ScriptEvent.ExportDocument, _open);
            Check(noticeCount == 1 && !firstEvent.IsCompleted && !secondEvent.IsCompleted, "Events were dropped or recursively dispatched.");
            noticeGate.SetResult(); Finish(Task.WhenAll(firstEvent, secondEvent));
            Check(noticeCount == 2, "An independent event was lost while a script awaited a notice.");

            // Repeated save commands during a Start Save notice share one save operation.
            _scriptEventSettings = ScriptEvents.Add(ScriptEventSettings.Default,
                new(ScriptEvent.StartSaveDocument, ScriptEventTarget.Script, EventScript.Welcome));
            noticeGate = new TaskCompletionSource(); noticeCount = 0;
            ScriptEventNotice = (_, _) => { noticeCount++; return noticeGate.Task; };
            var firstSave = SaveTab(_open, false); var secondSave = SaveTab(_open, false);
            Check(noticeCount == 1 && !secondSave.IsCompleted, "A concurrent save bypassed the pending Start Save event.");
            noticeGate.SetResult(); Finish(Task.WhenAll(firstSave, secondSave));
            Check(noticeCount == 1 && _open.Saving is null, "Concurrent saves duplicated the event or left a save pending.");

            ScriptEventNotice = (title, _) => { notices.Add(title); return Task.CompletedTask; };
            _scriptEventSettings = ScriptEvents.Add(ScriptEventSettings.Default,
                new(ScriptEvent.CloseDocument, ScriptEventTarget.Script, EventScript.Welcome));
            notices.Clear(); Finish(RaiseClosingDocumentEvents());
            Check(notices.Count == _tabs.Where(t => t.Document is not null).Select(t => t.Document).Distinct().Count(),
                "Window close did not dispatch once per document.");
            Finish(CloseTab(_open, true));

            _scriptEventSettings = settings with { Enabled = false };
            notices.Clear();
            Command("Auto Tone", () => { }).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); pump();
            Check(notices.Count == 0, "Disabled events still ran.");
        }
        finally
        {
            ScriptEventNotice = null;
            _scriptEventSettings = ScriptEventSettings.Default;
            try { File.Delete(ScriptEvents.SettingsPath); ScriptEvents.CleanLog(); Directory.Delete(folder, true); } catch (IOException) { }
        }
        Console.WriteLine("PASS: Script Events Manager layout, persistence, undo, exactly-once project open, placement/failure exclusions, Open As Layer, queued independent events, concurrent save guard and window-close dispatch.");
    }
}
