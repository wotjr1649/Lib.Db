using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Lib.Db.Verification.Tests.Infrastructure;

public class InMemoryLogger<T> : ILogger<T>, IDisposable
{
    private readonly ConcurrentBag<LogEntry> _logs = new();

    public IEnumerable<LogEntry> Logs => _logs;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _logs.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    public void Dispose() { }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}

public class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, object> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return (ILogger)_loggers.GetOrAdd(categoryName, _ => 
        {
            var loggerType = typeof(InMemoryLogger<>).MakeGenericType(typeof(object)); // Generic hack not needed if specific T not required, but strict typing is good. 
            // Simplifying: just return a generic implementation or make InMemoryLogger non-generic.
            // Let's make a non-generic version for the Provider to return easily.
            return new InMemoryLoggerImpl(categoryName, this);
        });
    }

    public InMemoryLoggerImpl GetLogger(string categoryName)
    {
        // Try to get existing logger
        if (_loggers.TryGetValue(categoryName, out var logger))
        {
            return (InMemoryLoggerImpl)logger;
        }
        return (InMemoryLoggerImpl)CreateLogger(categoryName);
    }

    public void Dispose() { }

    public class InMemoryLoggerImpl : ILogger
    {
        private readonly string _categoryName;
        private readonly InMemoryLoggerProvider _provider;
        public ConcurrentBag<LogEntry> Logs { get; } = new();

        public InMemoryLoggerImpl(string categoryName, InMemoryLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception, _categoryName));
        }
    }
}

public record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception, string? Category = null);
