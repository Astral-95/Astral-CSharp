using System.Diagnostics;

namespace Astral.Logging;

public enum ELogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public class AstralLogger
{
    public string Name { get; set; }

    private static readonly ReaderWriterLockSlim _Lock = new ReaderWriterLockSlim();

    public readonly Action<LogEntry>? OnLog;

    internal AstralLogger(string Name) { this.Name = Name; }

    public virtual void Log(ELogLevel Level, string Message)
    {
        var Entry = LogEntry.Rent(Name, Level, Message);

        _Lock.EnterWriteLock();
        try
        {
            OnLog?.Invoke(Entry);
        }
        finally
        {
            _Lock.ExitWriteLock();
        }

        AstralLoggingCenter.EnqueueLog(Entry);
    }


    [Conditional("CFG_LOG_TRACE")]
    public virtual void LogTrace(string Message) { Log(ELogLevel.Trace, Message); }

    [Conditional("CFG_LOG_DEBUG")]
    public virtual void LogDebug(string Message) { Log(ELogLevel.Debug, Message); }

    [Conditional("CFG_LOG_INFO")]
    public virtual void LogInfo(string Message) { Log(ELogLevel.Info, Message); }

    [Conditional("CFG_LOG_WARN")]
    public virtual void LogWarning(string Message) { Log(ELogLevel.Warning, Message); }

    [Conditional("CFG_LOG_ERROR")]
    public virtual void LogError(string Message) { Log(ELogLevel.Error, Message); }
    public virtual void LogCritical(string Message) { Log(ELogLevel.Critical, Message); }
}