using System.Net;

namespace Astral.Network;

public static class AddressConverters
{
    public static string IPEndPointToString(IPEndPoint EndPoint) => $"{EndPoint.Address}:{EndPoint.Port}";

    public static IPEndPoint StringToIPEndPoint(string value)
    {
        var Parts = value.Split(':');
        return new IPEndPoint(IPAddress.Parse(Parts[0]), int.Parse(Parts[1]));
    }


    public static IPEndPoint? ParseEndPoint(string EndPointString)
    {
        if (EndPointString.StartsWith("["))
        {
            // IPv6 with brackets, e.g. [::1]:8080
            int Index = EndPointString.IndexOf(']');
            if (Index == -1)
            {
                Console.WriteLine("Invalid IPv6 endpoint format.");
                return null;
            }

            string AddressPart = EndPointString.Substring(1, Index - 1);
            string PortPart = EndPointString.Substring(Index + 2);

            return new IPEndPoint(IPAddress.Parse(AddressPart), int.Parse(PortPart));
        }
        else
        {
            // IPv4 or bare IPv6 without port
            var Parts = EndPointString.Split(':');
            if (Parts.Length != 2)
            {
                Console.WriteLine("Invalid endpoint format.");
                return null;
            }

            return new IPEndPoint(IPAddress.Parse(Parts[0]), int.Parse(Parts[1]));
        }
    }
}
