using Astral.Diagnostics;
using System.Collections.Concurrent;

namespace Astral.Logging;

public class LogEntry
{
    protected int InPool = 0;

    private static readonly ConcurrentBag<LogEntry> Pool = new();


    public ELogLevel Level { get; set; }
    public string Name { get; set; }
    public string Message { get; set; }

    public DateTime Date { get; set; }

#pragma warning disable CS8618
    LogEntry() { }
#pragma warning restore CS8618
    LogEntry(string Name, ELogLevel Level, string Message)
    {
        this.Name = Name;
        this.Level = Level;
        this.Message = Message;
        Date = DateTime.Now;
    }

    public static LogEntry Rent()
    {
        if (!Pool.TryTake(out var Entry))
        {
            Entry = new LogEntry();
            PooledObjectsTracker.OnNewPoolObject();
        }

        Entry.InPool = 0;
#if !RELEASE
        PooledObjectsTracker.Register(Entry);
#endif
        return Entry;
    }

    public static LogEntry Rent(string Name, ELogLevel Level, string Message)
    {
        if (!Pool.TryTake(out var Entry))
        {
            Entry = new LogEntry(Name, Level, Message);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Entry.InPool = 0;
            Entry.Name = Name;
            Entry.Level = Level;
            Entry.Message = Message;
            Entry.Date = DateTime.Now;
        }
#if !RELEASE
        PooledObjectsTracker.Register(Entry);
#endif
        return Entry;
    }

    public void Return()
    {
#if !RELEASE
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        Guard.Assert(Val == 0, "Attempted to return an object that is already in the pool");
        PooledObjectsTracker.Unregister(this);
#endif
        Pool.Add(this);
    }
}