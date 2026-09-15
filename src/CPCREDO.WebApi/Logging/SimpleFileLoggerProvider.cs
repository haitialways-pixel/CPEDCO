using Microsoft.Extensions.Logging;

namespace CPCREDO.WebApi.Logging;

internal sealed class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();

    public SimpleFileLoggerProvider(string path)
    {
        _path = path;
    }

    public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(categoryName, _path, _gate);

    public void Dispose()
    {
    }

    private sealed class SimpleFileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _path;
        private readonly object _gate;

        public SimpleFileLogger(string category, string path, object gate)
        {
            _category = category;
            _path = path;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_category} {formatter(state, exception)}";
                if (exception is not null)
                    line += Environment.NewLine + exception;
                line += Environment.NewLine;
                lock (_gate)
                {
                    File.AppendAllText(_path, line);
                }
            }
            catch
            {
                // Logging must never take down the host.
            }
        }
    }
}
