namespace Compositor.Core.IO;

/// <summary>Publishes a completed output on the same volume. Failed encodes never truncate an existing file.</summary>
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> encode, CancellationToken cancellation = default)
    {
        var target = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encode(stream);
                cancellation.ThrowIfCancellationRequested();
                stream.Flush(flushToDisk: true);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static Task WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellation = default) =>
        Compositor.Core.Model.WorkScheduler.Run(_ => { WriteBytes(path, bytes, cancellation); return true; }, cancellation);

    public static void WriteBytes(string path, byte[] bytes, CancellationToken cancellation = default) =>
        Write(path, stream => stream.Write(bytes), cancellation);
}
