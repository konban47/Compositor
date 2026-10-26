using System.Diagnostics;
using System.Globalization;
using Compositor.Core.Document;

namespace Compositor.Desktop;

/// <summary>Optional, hidden ffplay processes follow the playhead and are killed when playback stops.</summary>
internal sealed class TimelineAudioPlayer : IDisposable
{
    private readonly Dictionary<Guid, Process> _players = [];
    private double _last = -1;
    internal void Update(DocumentTimeline timeline, double time)
    {
        if (!timeline.AudioEnabled) { Dispose(); return; }
        if (time < _last || time - _last > 1) Dispose();
        _last = time;
        var active = timeline.Audio.Where(a => !a.Muted && a.Start <= time && time < a.Start + a.Duration).ToArray();
        foreach (var id in _players.Keys.Except(active.Select(a => a.ID)).ToArray()) Stop(id);
        foreach (var track in active.Where(a => !_players.ContainsKey(a.ID)))
        {
            if (!Path.IsPathFullyQualified(track.File) || !File.Exists(track.File)) throw new IOException("Linked audio is missing.");
            string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
            var info = new ProcessStartInfo("ffplay") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-v", "error", "-nodisp", "-autoexit", "-protocol_whitelist", "file,pipe", "-ss", N(track.SourceStart + time - track.Start), "-t", N(track.Start + track.Duration - time), "-af", "volume=" + N(track.Volume), track.File }) info.ArgumentList.Add(argument);
            try { var process = Process.Start(info)!; _players[track.ID] = process; _ = process.StandardError.ReadToEndAsync(); _ = process.StandardOutput.ReadToEndAsync(); }
            catch (System.ComponentModel.Win32Exception) { throw new IOException("Install ffplay on PATH for timeline audio playback."); }
        }
    }
    private void Stop(Guid id)
    {
        var process = _players[id]; _players.Remove(id);
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
    public void Dispose() { foreach (var id in _players.Keys.ToArray()) Stop(id); _last = -1; }
}
