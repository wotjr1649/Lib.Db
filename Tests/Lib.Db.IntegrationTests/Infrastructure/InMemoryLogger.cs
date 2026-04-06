// ============================================================================
// 파일: Infrastructure/InMemoryLogger.cs
// 설명: 테스트용 인메모리 로거 (로그 캡처 및 검증)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// 제네릭 인메모리 로거.
/// </summary>
public sealed class InMemoryLogger<T> : ILogger<T>, IDisposable
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

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}

/// <summary>
/// 인메모리 로거 프로바이더.
/// </summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, object> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return (ILogger)_loggers.GetOrAdd(categoryName, _ =>
        {
            return new InMemoryLoggerImpl(categoryName, this);
        });
    }

    public InMemoryLoggerImpl GetLogger(string categoryName)
    {
        if (_loggers.TryGetValue(categoryName, out object? logger))
        {
            return (InMemoryLoggerImpl)logger;
        }
        return (InMemoryLoggerImpl)CreateLogger(categoryName);
    }

    public void Dispose() { }

    public sealed class InMemoryLoggerImpl : ILogger
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

/// <summary>
/// 로그 엔트리 레코드.
/// </summary>
public record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception, string? Category = null);
