using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace DocumentProcessor.WebForms.Logging
{
    /// <summary>
    /// Writes log entries to a single text file with every write flushed straight away. It
    /// replaces the Global.asax TextWriterTraceListener on ~/App_Data/trace.log
    /// (Trace.AutoFlush = true), so the error page's "written to the application trace log"
    /// is still true. The file stays outside wwwroot, so it is never served.
    /// </summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly object _gate = new object();
        private readonly StreamWriter _writer;

        public FileLoggerProvider(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream) { AutoFlush = true };
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(this, categoryName);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _writer.Dispose();
            }
        }

        private void Write(string line)
        {
            lock (_gate)
            {
                _writer.WriteLine(line);
            }
        }

        private sealed class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _provider;
            private readonly string _category;

            public FileLogger(FileLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

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

                var line = DateTimeOffset.UtcNow.ToString("O") + " " + logLevel + " " + _category + ": "
                    + formatter(state, exception);

                if (exception != null)
                {
                    line += Environment.NewLine + exception;
                }

                _provider.Write(line);
            }
        }
    }
}
