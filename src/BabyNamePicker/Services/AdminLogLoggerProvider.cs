using Microsoft.Extensions.Logging;

namespace BabyNamePicker.Services;

/// <summary>
/// Forwards Warning+ log events into the admin console ring buffer.
/// </summary>
public sealed class AdminLogLoggerProvider(AdminLogHub hub) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new AdminLogLogger(categoryName, hub);

    public void Dispose()
    {
    }

    private sealed class AdminLogLogger(string categoryName, AdminLogHub hub) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message} | {exception.GetType().Name}: {exception.Message}";
            }

            var source = ShortSource(categoryName);
            var level = logLevel switch
            {
                LogLevel.Critical or LogLevel.Error => "Error",
                _ => "Warning"
            };

            hub.Write(level, source, message);
        }

        private static string ShortSource(string category)
        {
            var lastDot = category.LastIndexOf('.');
            return lastDot >= 0 && lastDot < category.Length - 1
                ? category[(lastDot + 1)..]
                : category;
        }
    }
}
