using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Logging;

public class ScrubbingLogger : ILogger
{
    private readonly ILogger _innerLogger;
    private readonly IPhiScrubber _scrubber;

    public ScrubbingLogger(ILogger innerLogger, IPhiScrubber scrubber)
    {
        _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
        _scrubber = scrubber ?? throw new ArgumentNullException(nameof(scrubber));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
        {
            var scrubbed = _scrubber.ScrubProperties(kvps);
            return _innerLogger.BeginScope(scrubbed);
        }

        if (state is string strState)
        {
            return _innerLogger.BeginScope(_scrubber.ScrubText(strState));
        }

        return _innerLogger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var sanitizedException = _scrubber.ScrubException(exception);

        if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
        {
            var scrubbedDict = _scrubber.ScrubProperties(kvps);
            _innerLogger.Log(
                logLevel,
                eventId,
                scrubbedDict,
                sanitizedException,
                (s, ex) =>
                {
                    var msg = formatter(state, ex);
                    return _scrubber.ScrubText(msg);
                });
        }
        else
        {
            _innerLogger.Log(
                logLevel,
                eventId,
                state,
                sanitizedException,
                (s, ex) =>
                {
                    var msg = formatter(s, ex);
                    return _scrubber.ScrubText(msg);
                });
        }
    }
}
