namespace Astral.Network.Tests.Tools;

public class LogStruct
{
    public string? Name { get; set; }
    public Astral.Logging.ELogLevel Level { get; set; }
    public string Message { get; set; }
    public DateTime? Date { get; set; }

    public LogStruct(string? name, Astral.Logging.ELogLevel level, string message, DateTime? date)
    {
        Name = name;
        Level = level;
        Message = message;
        Date = date;
    }
}