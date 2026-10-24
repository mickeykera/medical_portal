using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Logging;

public static class LoggingExtensions
{
    public static ILoggingBuilder AddPhiScrubber(this ILoggingBuilder builder, Action<string>? logSink = null)
    {
        builder.Services.AddSingleton<IPhiScrubber, PhiScrubber>();
        builder.Services.AddSingleton<ILoggerProvider>(sp =>
        {
            var scrubber = sp.GetRequiredService<IPhiScrubber>();
            return new ScrubbingLoggerProvider(scrubber, logSink);
        });
        return builder;
    }
}

public class ScrubbingLoggerProvider : ILoggerProvider
{
    private readonly IPhiScrubber _scrubber;
    private readonly Action<string>? _logSink;

    public ScrubbingLoggerProvider(IPhiScrubber scrubber, Action<string>? logSink = null)
    {
        _scrubber = scrubber;
        _logSink = logSink;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ScrubbingSinkLogger(categoryName, _scrubber, _logSink);
    }

    public void Dispose() { }
}

public class ScrubbingSinkLogger : ILogger
{
    private readonly string _categoryName;
    private readonly IPhiScrubber _scrubber;
    private readonly Action<string>? _sink;

    public ScrubbingSinkLogger(string categoryName, IPhiScrubber scrubber, Action<string>? sink = null)
    {
        _categoryName = categoryName;
        _scrubber = scrubber;
        _sink = sink;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var scrubbedMessage = _scrubber.ScrubText(message);

        var scrubbedException = _scrubber.ScrubException(exception);
        var exDetails = scrubbedException != null ? $"\nException: {scrubbedException.Message}\n{scrubbedException.StackTrace}" : "";

        var output = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {logLevel}] [{_categoryName}] {scrubbedMessage}{exDetails}";

        if (_sink != null)
        {
            _sink(output);
        }
        else
        {
            Console.WriteLine(output);
        }
    }
}
