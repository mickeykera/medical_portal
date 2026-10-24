namespace MedicalPortal.Infrastructure.DistributedLock;

/// <summary>
/// Represents an acquired distributed lock handle.
/// Disposing this handle releases the lock cluster-wide.
/// </summary>
public interface IDistributedLock : IAsyncDisposable, IDisposable
{
    string ResourceName { get; }
    bool IsAcquired { get; }
}

/// <summary>
/// Coordinates cluster-wide exclusive execution for background workers and scheduled jobs
/// across multiple application instances.
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// Attempts to acquire an exclusive lock for the specified resource name.
    /// If another instance holds the lock, returns immediately with IsAcquired = false.
    /// </summary>
    Task<IDistributedLock> TryAcquireLockAsync(
        string resourceName,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default);
}
