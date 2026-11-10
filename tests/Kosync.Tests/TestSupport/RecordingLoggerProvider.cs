using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Kosync.Tests.TestSupport;

// Keeps every log entry the app writes so a test can assert on what an
// operator would see. The Sync Trace is the one feature whose entire
// observable behaviour *is* its log output (issue #41 follow-up), so for it
// this is the seam, not a side channel.
//
// Registered on every factory. It holds a bounded list of short strings and
// nothing reads it unless a test asks, so the cost to the rest of the suite
// is noise-level.
public class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedLog> _entries = new();

    public IReadOnlyList<RecordedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) =>
        new RecordingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<RecordedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new RecordedLog(logLevel, category, formatter(state, exception)));
        }
    }
}

public record RecordedLog(LogLevel Level, string Category, string Message);
