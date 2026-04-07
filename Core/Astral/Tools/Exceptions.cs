namespace Astral.Exceptions;

public class AlreadyInPoolException : Exception
{
    public AlreadyInPoolException(string Message)
        : base(Message) { }
}