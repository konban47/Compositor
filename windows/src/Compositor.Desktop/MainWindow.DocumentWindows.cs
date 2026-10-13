using Avalonia;
using Avalonia.Controls;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private static readonly List<MainWindow> Editors = [];
    private static bool _syncingEditors;
    private static readonly Dictionary<Tab, (MainWindow Owner, int Count)> SharedEditLocks = [];
    private bool _sharedViewBlocked;
    private IDisposable? _sharedPreviewLease;
    private sealed class SharedEditLease(Action release) : IDisposable
    { private Action? _release = release; public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke(); }
    private IDisposable LockSharedEdit()
    {
        var tab = _open;
        if (SharedEditLocks.TryGetValue(tab, out var current) && !ReferenceEquals(current.Owner, this)) throw new InvalidOperationException("Finish editing in the other document window first.");
        SharedEditLocks[tab] = (this, current.Count + 1); UpdateSharedViewAccess();
        return new SharedEditLease(() => { if (SharedEditLocks.TryGetValue(tab, out var active) && ReferenceEquals(active.Owner, this)) { if (active.Count == 1) SharedEditLocks.Remove(tab); else SharedEditLocks[tab] = (this, active.Count - 1); } UpdateSharedViewAccess(); });
    }
    private static void UpdateSharedViewAccess()
    {
        foreach (var editor in Editors)
        {
            var blocked = SharedEditLocks.TryGetValue(editor._open, out var edit) && !ReferenceEquals(edit.Owner, editor);
            if (blocked == editor._sharedViewBlocked) continue;
            editor._sharedViewBlocked = blocked; if (editor.Content is Control content) content.IsEnabled = !blocked;
        }
    }
    private void InitializeDocumentWindows()
    {
        Editors.Add(this);
        Activated += (_, _) => { foreach (var editor in Editors.Where(e => !ReferenceEquals(e, this) && ReferenceEquals(e._open, _open)).ToArray()) editor.CommitText(); };
        Closed += (_, _) => { Editors.Remove(this); foreach (var tab in SharedEditLocks.Where(p => ReferenceEquals(p.Value.Owner, this)).Select(p => p.Key).ToArray()) SharedEditLocks.Remove(tab); UpdateSharedViewAccess(); _watchTimer?.Stop(); };
    }
    private bool HasOtherView(Tab tab) => Editors.Any(e => !ReferenceEquals(e, this) && e._tabs.Contains(tab));
    private void RefreshDocumentWindows()
    {
        UpdateSharedViewAccess();
        if (_syncingEditors) return;
        _syncingEditors = true;
        try
        {
            foreach (var editor in Editors.Where(e => !ReferenceEquals(e, this) && e._tabs.Contains(_open)).ToArray())
            {
                editor.RefreshTabs();
                if (!ReferenceEquals(editor._open, _open)) continue;
                editor._canvas.Document = _document;
                if (_document is { } doc) editor.ShowLayers(doc);
                editor.Refresh();
            }
        }
        finally { _syncingEditors = false; }
    }
    private MainWindow DocumentWindow(Tab tab, bool move)
    {
        CommitText(); StopPreview();
        var view = _canvas.Viewport; var angle = _canvas.ViewAngle;
        var child = new MainWindow(); child._tabs.Clear(); child._tabs.Add(tab); child._open = tab;
        if (move) { _tabs.Remove(tab); if (_tabs.Count == 0) _tabs.Add(new Tab()); _open = _tabs[0]; Show(_open); }
        child.Show(); child.Bring(tab); child.Position = new(Position.X + 36, Position.Y + 36);
        child._canvas.RestoreViewport(view); child._canvas.RotateViewTo(angle);
        return child;
    }
    private void FloatDocuments()
    {
        foreach (var tab in _tabs.Where(t => t.Document is not null).Skip(1).ToArray()) DocumentWindow(tab, true);
    }
    private void ConsolidateDocuments()
    {
        foreach (var other in Editors.Where(e => !ReferenceEquals(e, this)).ToArray())
        {
            other.CommitText(); other.StopPreview();
            foreach (var tab in other._tabs.Where(t => t.Document is not null).ToArray()) { other._tabs.Remove(tab); if (!_tabs.Contains(tab)) _tabs.Add(tab); }
            other._closeApproved = true; other.Close();
        }
        Bring(_open); Activate();
    }
    private void ArrangeDocuments(string mode)
    {
        FloatDocuments();
        var windows = Editors.Where(e => e._document is not null).ToArray(); if (windows.Length == 0) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary; if (screen is null) return;
        var area = screen.WorkingArea; var scale = screen.Scaling;
        var columns = mode switch { "Tile All Vertically" => windows.Length, "Tile All Horizontally" or "2-up Horizontal" or "3-up Horizontal" => 1, "2-up Vertical" => 2, "3-up Vertical" or "6-up" => 3, _ => 2 };
        var rows = (int)Math.Ceiling(windows.Length / (double)columns);
        for (var i = 0; i < windows.Length; i++)
        {
            var w = windows[i]; w.WindowState = WindowState.Normal; w.MinWidth = 240; w.MinHeight = 180;
            if (mode == "Cascade") { w.Width = area.Width / scale * .78; w.Height = area.Height / scale * .78; w.Position = new(area.X + i * 28, area.Y + i * 28); }
            else if (mode == "3-up Stacked" && windows.Length > 1)
            { var width = area.Width / 2; var height = i == 0 ? area.Height : area.Height / (windows.Length - 1); w.Width = width / scale; w.Height = height / scale; w.Position = new(area.X + (i == 0 ? 0 : width), area.Y + (i == 0 ? 0 : (i - 1) * height)); }
            else { var width = area.Width / columns; var height = area.Height / rows; w.Width = width / scale; w.Height = height / scale; w.Position = new(area.X + i % columns * width, area.Y + i / columns * height); }
            if (w.Width < 800) w._layersSide.IsVisible = false;
        }
    }
    private void MatchDocumentViews(string kind)
    {
        var view = _canvas.Viewport;
        foreach (var other in Editors.Where(e => !ReferenceEquals(e, this) && e._document is not null))
        {
            var old = other._canvas.Viewport;
            other._canvas.RestoreViewport((kind is "Match Zoom" or "Match All" ? view.Zoom : old.Zoom,
                kind is "Match Location" or "Match All" ? view.OriginX : old.OriginX, kind is "Match Location" or "Match All" ? view.OriginY : old.OriginY));
            if (kind is "Match Rotation" or "Match All") other._canvas.RotateViewTo(_canvas.ViewAngle);
        }
    }
}
