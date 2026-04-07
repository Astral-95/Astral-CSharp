using Astral.Logging;
using System.Diagnostics;

namespace Astral.Network.Logging;

public class NetaLogger
{
    public string Name { get; set; }

    private static readonly ReaderWriterLockSlim _Lock = new ReaderWriterLockSlim();

    public readonly Action<LogEntry>? OnLog;

    public NetaLogger(string Name) { this.Name = Name; }

    public void Log(ELogLevel Level, string Message)
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
    public void LogTrace(string Message) { Log(ELogLevel.Trace, Message); }

    [Conditional("CFG_LOG_DEBUG")]
    public void LogDebug(string Message) { Log(ELogLevel.Debug, Message); }

    [Conditional("CFG_LOG_INFO")]
    public void LogInfo(string Message) { Log(ELogLevel.Info, Message); }

    [Conditional("CFG_LOG_WARN")]
    public void LogWarning(string Message) { Log(ELogLevel.Warning, Message); }

    [Conditional("CFG_LOG_ERROR")]
    public void LogError(string Message) { Log(ELogLevel.Error, Message); }
    public void LogCritical(string Message) { Log(ELogLevel.Critical, Message); }
}