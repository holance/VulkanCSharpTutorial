using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace VulkanCSharpTutorial
{
    /// <summary>
    /// 
    /// </summary>
    public static class LogManager
    {
        public static ILoggerFactory Factory
        {
            set; get;
        } = new DebugLoggerFactory();

        public static ILogger Create<T>()
        {
            return Factory.CreateLogger<T>();
        }

        public static ILogger Create(string category)
        {
            return Factory.CreateLogger(category);
        }
    }

    internal sealed class DebugLoggerFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
            // Method intentionally left empty.
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DebugLogger(categoryName);
        }

        public void Dispose()
        {
            // Method intentionally left empty.
        }
    }
    /// <summary>
    /// 
    /// </summary>
    internal sealed class DebugLogger : ILogger
    {
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            private NullScope()
            {
            }
            public void Dispose()
            {
                // Method intentionally left empty.
            }
        }
        private readonly string categoryName_;
        public DebugLogger(string categoryName)
        {
            categoryName_ = categoryName;
        }
        public IDisposable BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel > LogLevel.Trace;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Debug.WriteLine("Level: {0}; Class: {1}; Msg: {2}", logLevel, categoryName_, exception == null ? state : formatter.Invoke(state, exception));
        }
    }
}
