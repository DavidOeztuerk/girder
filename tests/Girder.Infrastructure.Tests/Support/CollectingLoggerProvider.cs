using System.Text;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Support;

/// <summary>
/// Keeps every log entry, so a test can look for what should not be there.
/// </summary>
/// <remarks>
/// Structured values are appended as well as the message: a body or a payload
/// travels as a log property, and a test that read only the formatted message
/// would miss exactly what it is looking for.
/// </remarks>
public sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _entries = [];
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Entries
    {
        get { lock (_entries) { return _entries.ToArray(); } }
    }

    public IReadOnlyList<string> Warnings
    {
        get { lock (_entries) { return _warnings.ToArray(); } }
    }

    public ILogger CreateLogger(string categoryName) => new Collector(_entries, _warnings);

    public void Dispose() { }

    private sealed class Collector(List<string> entries, List<string> warnings) : ILogger
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
            var line = new StringBuilder(formatter(state, exception));

            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var value in values)
                {
                    line.Append(' ').Append(value.Value);
                }
            }

            lock (entries)
            {
                entries.Add(line.ToString());

                if (logLevel >= LogLevel.Warning)
                {
                    warnings.Add(line.ToString());
                }
            }
        }
    }
}
