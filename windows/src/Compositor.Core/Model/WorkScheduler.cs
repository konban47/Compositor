namespace Compositor.Core.Model;

/// <summary>Bounded expensive CPU work. Cancellation removes obsolete queued work before it starts.</summary>
public static class WorkScheduler
{
    private static readonly SemaphoreSlim Slots = new(Math.Clamp(Environment.ProcessorCount / 4, 1, 2));
    public static async Task<T> Run<T>(Func<CancellationToken, T> work, CancellationToken cancellation = default)
    {
        await Slots.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await Task.Run(() => { cancellation.ThrowIfCancellationRequested(); return work(cancellation); }, cancellation).ConfigureAwait(false); }
        finally { Slots.Release(); }
    }
}
