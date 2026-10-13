using Avalonia.Threading;
using Compositor.Core.IO;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private DispatcherTimer? _recoveryTimer;
    private bool _recovering;
    private readonly Dictionary<Tab, (Guid Document, Guid Revision)> _recovered = [];
    private static string RecoveryPath(Tab tab, Guid document) => Path.Combine(RecoveryFolder, $"{document:N}-{tab.RecoveryID:N}.comp");
    private static string RecoveryFolder => Path.Combine(AppPaths.SettingsDirectory, "recovery");
    private void ConfigureRecovery()
    {
        _recoveryTimer?.Stop();
        if (!EditorPreferences.Current.RecoveryEnabled) return;
        if (_recoveryTimer is null) { _recoveryTimer = new DispatcherTimer(); _recoveryTimer.Tick += async (_, _) => await SaveRecoveryCopies(); }
        _recoveryTimer.Interval = TimeSpan.FromMinutes(EditorPreferences.Current.RecoveryMinutes); _recoveryTimer.Start();
    }
    internal async Task SaveRecoveryCopies()
    {
        if (_recovering || _placementTab is not null || _text is not null) return;
        _recovering = true;
        try
        {
            foreach (var tab in _tabs.ToArray())
            {
                if (!_tabs.Contains(tab) || tab.Document is not { } doc || !tab.History.IsModified || tab.Saving is not null) continue;
                var identity = (doc.ID, tab.History.CurrentRevision);
                if (_recovered.TryGetValue(tab, out var saved) && saved == identity) continue;
                // Same snapshot/immutable-asset ownership as ordinary saves. Closing a tab awaits recovery without swallowing a manual save.
                var snapshot = ProjectSnapshot.FromDocument(doc.Clone());
                Directory.CreateDirectory(RecoveryFolder);
                var path = RecoveryPath(tab, doc.ID);
                var task = Task.Run(() => { ProjectStore.Save(snapshot, path); return true; }); tab.Recovering = task;
                try { await task; _recovered[tab] = identity; }
                finally { if (ReferenceEquals(tab.Recovering, task)) tab.Recovering = null; }
            }
            foreach (var tab in _recovered.Keys.Where(t => !_tabs.Contains(t)).ToArray()) _recovered.Remove(tab);
        }
        catch (Exception error) { Say(Localize.Text("Could not save recovery copy.") + " " + error.Message); }
        finally { _recovering = false; }
    }
    private void OpenRecoveryFolder()
    {
        try { Directory.CreateDirectory(RecoveryFolder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RecoveryFolder) { UseShellExecute = true }); }
        catch (Exception error) { Say(error.Message); }
    }
    private static void RecordHistoryLog(string command)
    {
        if (!EditorPreferences.Current.HistoryLog) return;
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDirectory); var path = Path.Combine(AppPaths.SettingsDirectory, "history.log");
            if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}\t{command.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(error.Message); }
    }
    private void SavePreferenceChanges() { try { EditorPreferences.Current.Save(); } catch (Exception error) { Say(error.Message); } }
    private void ResetWorkspace()
    {
        ResetDockPanels();
        ExitScreenMode(); foreach (var control in _chrome) control.IsVisible = true;
        var layout = ToolCatalog.Defaults(); layout.Save(ToolbarLayout.DefaultPath, ToolCatalog.IDs);
        _rail.ApplyLayout(layout); _rail.ShowColours(BrushColour(), BackgroundColour()); _rail.ShowShortcuts(_keys);
        _optionsBar.IsVisible = true; OpenInspector(0); _canvas.Fit();
    }
}
