using Astral.Tools;
using System.Threading.Channels;

namespace Astral.Logging;

public static class AstralLoggingCenter
{
    private static readonly SynchronizationContext ThreadContext;

    static Channel<LogEntry> LogChannel = Channel.CreateUnbounded<LogEntry>();

    private static readonly List<WeakAction<LogEntry>> LoggingSubscribers = new();
    private static readonly Dictionary<ELogLevel, List<WeakAction<LogEntry>>> LoggingByLevelSubscribers = new();

    private static readonly ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();
    static AstralLoggingCenter()
    {
        ThreadContext = SynchronizationContext.Current ?? new SynchronizationContext();

        LoggingByLevelSubscribers.Add(ELogLevel.Trace, new());
        LoggingByLevelSubscribers.Add(ELogLevel.Debug, new());
        LoggingByLevelSubscribers.Add(ELogLevel.Info, new());
        LoggingByLevelSubscribers.Add(ELogLevel.Warning, new());
        LoggingByLevelSubscribers.Add(ELogLevel.Error, new());
        LoggingByLevelSubscribers.Add(ELogLevel.Critical, new());

        Task.Run(LogTaskAsync);
    }

    static async Task LogTaskAsync()
    {
        await foreach (var Entry in LogChannel.Reader.ReadAllAsync())
        {
            lock (Lock)
            {
                try
                {
                    foreach (var Sub in LoggingSubscribers) Sub.Invoke(Entry);

                    if (LoggingByLevelSubscribers.TryGetValue(Entry.Level, out var Subs))
                    {
                        foreach (var Sub in Subs) Sub.Invoke(Entry);
                    }
                }
                catch (Exception) { }
            }

            Entry.Return();
        }
    }

    public static AstralLogger CreateLogger(string Name)
    {
        return new AstralLogger(Name);
    }

    public static void Subscribe(Action<LogEntry> Callback)
    {
        lock (Lock) LoggingSubscribers.Add(new WeakAction<LogEntry>(Callback));
    }

    public static void Unsubscribe(Action<ELogLevel, string> Callback)
    {
        lock (Lock) LoggingSubscribers.RemoveAll(sub => sub == Callback);
    }

    public static void Subscribe(ELogLevel Loglevel, Action<LogEntry> Callback)
    {
        lock (Lock)
        {
            LoggingByLevelSubscribers.TryGetValue(Loglevel, out var List);
            List!.Add(new WeakAction<LogEntry>(Callback));
        }
    }

    public static void Unsubscribe(ELogLevel Loglevel, Action<string> Callback)
    {
        lock (Lock)
        {
            LoggingByLevelSubscribers.TryGetValue(Loglevel, out var List);
            List!.RemoveAll(sub => sub == Callback);
        }
    }

    public static void Log(string Context, ELogLevel Level, string Message)
    {
        var Entry = LogEntry.Rent(Context, Level, Message);

        EnqueueLog(Entry);
    }

    public static void EnqueueLog(LogEntry Entry) => LogChannel.Writer.TryWrite(Entry);
}

#if false
ConsoleColor OriginalColor = Console.ForegroundColor;

			string OutputLevelStr = "";

			switch (Level)
			{
				case ELogLevel.Trace:
					OutputLevelStr = "[Trace]";
					Console.ForegroundColor = ConsoleColor.White;
					break;
				case ELogLevel.Debug:
					OutputLevelStr = "[Debug]";
					Console.ForegroundColor = ConsoleColor.White;
					break;
				case ELogLevel.Info:
					OutputLevelStr = "[Info]";
					Console.ForegroundColor = ConsoleColor.Gray;
					break;
				case ELogLevel.Warning:
					OutputLevelStr = "[Warning]";
					Console.ForegroundColor = ConsoleColor.Yellow;
					break;
				case ELogLevel.Error:
					OutputLevelStr = "[Error]";
					Console.ForegroundColor = ConsoleColor.DarkRed;
					break;
				case ELogLevel.Critical:
					OutputLevelStr = "[Critical]";
					Console.ForegroundColor = ConsoleColor.Red;
					break;
				default:
					break;
			}
			Console.WriteLine($"{OutputLevelStr}: {FormattedMessage}");
			Console.ForegroundColor = OriginalColor;
#endif