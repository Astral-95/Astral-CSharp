using Astral.Network.Transport;

namespace Astral.Network.Exceptions;

public class InvalidPacketException : Exception
{
    public InvalidPacketException(string message)
        : base(message) { }
}