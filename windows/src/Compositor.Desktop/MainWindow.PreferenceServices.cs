using Avalonia.Threading;
using Compositor.Core.IO;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private DispatcherTimer? _recoveryTimer;
    private bool _recovering;
    private readonly Dictionary<Tab, (Guid Document, Guid Revision)> _recovered = [];
    private static string RecoveryPath(Tab tab, Guid document) => Path.Combine(RecoveryFolder, $"{document:N}-{tab.RecoveryID:N}.comp");
    /// <summary>Recovery copies go to the first active scratch disk Scratch Disks preferences list, or the settings folder.</summary>
    private static string RecoveryFolder => EditorPreferences.Current.ScratchDisks.FirstOrDefault(d => Directory.Exists(d)) is { } disk
        ? Path.Combine(disk, "Compositor Scratch", "recovery") : Path.Combine(AppPaths.SettingsDirectory, "recovery");
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
    /// <summary>History preferences' Edit Log Items: Sessions Only writes no commands, Concise each command, Detailed adds who made it.</summary>
    private static void RecordHistoryLog(string command)
    { if (EditorPreferences.Current.HistoryLogDetail > 0) WriteHistoryLog(command); }
    private static void WriteHistoryLog(string entry)
    {
        var p = EditorPreferences.Current;
        if (!p.HistoryLog) return;
        try
        {
            var path = HistoryLogFile; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024) File.Move(path, path + ".previous", true);
            var who = p.HistoryLogDetail == 2 ? $"\t{Environment.UserName}@{Environment.MachineName}" : "";
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}\t{entry.Replace('\r', ' ').Replace('\n', ' ')}{who}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(error.Message); }
    }
    /// <summary>The text file History preferences chose, or history.log in the settings folder.</summary>
    internal static string HistoryLogFile => EditorPreferences.Current.HistoryLogPath is { Length: > 0 } chosen ? chosen : Path.Combine(AppPaths.SettingsDirectory, "history.log");
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
