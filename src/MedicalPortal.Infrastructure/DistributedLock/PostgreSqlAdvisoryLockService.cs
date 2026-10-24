using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MedicalPortal.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace MedicalPortal.Infrastructure.DistributedLock;

/// <summary>
/// Distributed lock implementation utilizing PostgreSQL session-level advisory locks (pg_try_advisory_lock).
/// Guarantees that scheduled background workers and batch tasks run at most once cluster-wide across multiple instances.
/// Includes an in-memory fallback for offline SQLite development and testing.
/// </summary>
public class PostgreSqlAdvisoryLockService : IDistributedLockService
{
    private readonly string? _npgsqlConnectionString;
    private readonly ILogger<PostgreSqlAdvisoryLockService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _inMemoryLocks = new();

    public PostgreSqlAdvisoryLockService(
        IConfiguration configuration,
        ILogger<PostgreSqlAdvisoryLockService>? logger = null)
    {
        _logger = logger ?? NullLogger<PostgreSqlAdvisoryLockService>.Instance;

        var rawConn = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["DATABASE_URL"]
            ?? configuration["POSTGRES_CONNECTION_STRING"];

        if (PostgresConnectionHelper.IsPostgres(rawConn))
        {
            _npgsqlConnectionString = PostgresConnectionHelper.ToNpgsqlConnectionString(rawConn!);
        }
    }

    public PostgreSqlAdvisoryLockService(
        string? connectionString,
        ILogger<PostgreSqlAdvisoryLockService>? logger = null)
    {
        _logger = logger ?? NullLogger<PostgreSqlAdvisoryLockService>.Instance;

        if (PostgresConnectionHelper.IsPostgres(connectionString))
        {
            _npgsqlConnectionString = PostgresConnectionHelper.ToNpgsqlConnectionString(connectionString!);
        }
    }

    public async Task<IDistributedLock> TryAcquireLockAsync(
        string resourceName,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("ResourceName cannot be null or empty.", nameof(resourceName));

        // 1. If running on PostgreSQL, use real session-level advisory locks
        if (!string.IsNullOrWhiteSpace(_npgsqlConnectionString))
        {
            return await TryAcquirePostgresLockAsync(resourceName, cancellationToken);
        }

        // 2. Fallback to in-memory coordination (for local development or unit test environments)
        return await TryAcquireInMemoryLockAsync(resourceName, timeout, cancellationToken);
    }

    private async Task<IDistributedLock> TryAcquirePostgresLockAsync(string resourceName, CancellationToken cancellationToken)
    {
        var lockId = ComputeDeterministicLockId(resourceName);
        NpgsqlConnection? conn = null;

        try
        {
            conn = new NpgsqlConnection(_npgsqlConnectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key);", conn);
            cmd.Parameters.AddWithValue("key", lockId);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            var acquired = result is bool b && b;

            if (!acquired)
            {
                _logger.LogDebug("PostgreSQL advisory lock for {ResourceName} (ID: {LockId}) could not be acquired (held by another session).", resourceName, lockId);
                await conn.DisposeAsync();
                return new NonAcquiredLock(resourceName);
            }

            _logger.LogDebug("Acquired PostgreSQL advisory lock for {ResourceName} (ID: {LockId}).", resourceName, lockId);
            return new PostgresAdvisoryLock(resourceName, lockId, conn, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error attempting to acquire PostgreSQL advisory lock for {ResourceName}", resourceName);
            if (conn != null)
            {
                await conn.DisposeAsync();
            }
            return new NonAcquiredLock(resourceName);
        }
    }

    private async Task<IDistributedLock> TryAcquireInMemoryLockAsync(string resourceName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var semaphore = _inMemoryLocks.GetOrAdd(resourceName, _ => new SemaphoreSlim(1, 1));
        var waitTime = timeout > TimeSpan.Zero ? timeout : TimeSpan.Zero;

        var acquired = await semaphore.WaitAsync(waitTime, cancellationToken);
        if (!acquired)
        {
            _logger.LogDebug("In-memory lock for {ResourceName} was not acquired.", resourceName);
            return new NonAcquiredLock(resourceName);
        }

        _logger.LogDebug("Acquired in-memory lock for {ResourceName}.", resourceName);
        return new InMemoryLock(resourceName, semaphore, _logger);
    }

    public static long ComputeDeterministicLockId(string resourceName)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(resourceName.Trim().ToLowerInvariant()));
        return BitConverter.ToInt64(bytes, 0);
    }

    private sealed class PostgresAdvisoryLock : IDistributedLock
    {
        private readonly string _resourceName;
        private readonly long _lockId;
        private readonly NpgsqlConnection _connection;
        private readonly ILogger _logger;
        private int _disposed;

        public string ResourceName => _resourceName;
        public bool IsAcquired => true;

        public PostgresAdvisoryLock(string resourceName, long lockId, NpgsqlConnection connection, ILogger logger)
        {
            _resourceName = resourceName;
            _lockId = lockId;
            _connection = connection;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                {
                    await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key);", _connection);
                    cmd.Parameters.AddWithValue("key", _lockId);
                    await cmd.ExecuteNonQueryAsync();
                    _logger.LogDebug("Released PostgreSQL advisory lock for {ResourceName} (ID: {LockId}).", _resourceName, _lockId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release PostgreSQL advisory lock for {ResourceName}", _resourceName);
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class InMemoryLock : IDistributedLock
    {
        private readonly string _resourceName;
        private readonly SemaphoreSlim _semaphore;
        private readonly ILogger _logger;
        private int _disposed;

        public string ResourceName => _resourceName;
        public bool IsAcquired => true;

        public InMemoryLock(string resourceName, SemaphoreSlim semaphore, ILogger logger)
        {
            _resourceName = resourceName;
            _semaphore = semaphore;
            _logger = logger;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _semaphore.Release();
            _logger.LogDebug("Released in-memory lock for {ResourceName}.", _resourceName);
        }
    }

    private sealed class NonAcquiredLock : IDistributedLock
    {
        public string ResourceName { get; }
        public bool IsAcquired => false;

        public NonAcquiredLock(string resourceName)
        {
            ResourceName = resourceName;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
