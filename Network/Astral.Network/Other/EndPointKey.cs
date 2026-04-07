using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Astral.Network.Toolkit;

public readonly struct EndPointKey : IEquatable<EndPointKey>
{
    public readonly UInt128 Address;
    public readonly int Port;
    public readonly AddressFamily Family; // We need this to know the original "size"
    readonly int Hash;


    public EndPointKey(UInt128 Address, ushort Port)
    {
        this.Address = Address;
        this.Port = Port;
        Hash = HashCode.Combine(Address, Port);
    }
    public EndPointKey(IPEndPoint EP)
    {
        //Span<byte> bytes = stackalloc byte[16];
        //ep.Address.TryWriteBytes(bytes, out _);
        //Address = MemoryMarshal.Read<UInt128>(bytes);
        //Port = ep.Port;
        //Hash = HashCode.Combine(Address, Port);

        Port = EP.Port;
        Family = EP.Address.AddressFamily;

        Span<byte> Bytes = stackalloc byte[16];
        // 1. Write original bytes (4 for IPv4, 16 for IPv6)
        if (EP.Address.TryWriteBytes(Bytes, out int Written))
        {
            // 2. Read into UInt128 using BigEndian to keep it human-readable/consistent
            // We use the full 16-byte span; if it was IPv4, the rest is trailing zeros
            Address = BinaryPrimitives.ReadUInt128BigEndian(Bytes);
        }
        else
        {
            Address = 0;
        }

        Hash = HashCode.Combine(Address, Port, Family);
    }

    public bool Equals(EndPointKey Other) => Port == Other!.Port && Address == Other.Address;
    public override bool Equals(object? Obj) => Obj is EndPointKey k && Equals(k);
    public override int GetHashCode() => Hash;
    public override string ToString() => $"{GetAddressStringZero()}:{Port}";
    public IPEndPoint ToEndPoint() => new IPEndPoint(GetAddress(), Port);

    /// <summary>
    /// Gets the IPv4 address and Port in Network Byte Order without allocations.
    /// </summary>
    //public (uint rawIp, ushort rawPort) GetNetworkOrderData()
    //{
    //    // For IPv4, the address is stored in the first 4 bytes of the UInt128/Span
    //    // ep.Address.Address is deprecated but fast; GetAddressBytes allocates.
    //    // We can use the existing IPAddress object to write to a small stack span.
    //
    //    Span<byte> ipBytes = stackalloc byte[4];
    //    if (IPAddress.TryWriteBytes(ipBytes, out _))
    //    {
    //        uint ip = BinaryPrimitives.ReadUInt32LittleEndian(ipBytes);
    //        // Note: IPAddress.TryWriteBytes usually outputs in Network Order already.
    //        // But we need to be explicit to ensure the kernel reads it correctly.
    //
    //        return (
    //            ip,
    //            (ushort)IPAddress.HostToNetworkOrder((short)Port)
    //        );
    //    }
    //    return (0, 0);
    //}

    public IPAddress GetAddress()
    {
        Span<byte> Bytes = stackalloc byte[16];
        // 3. Write the UInt128 back to bytes
        BinaryPrimitives.WriteUInt128BigEndian(Bytes, Address);

        // 4. Slice the span based on the original Family
        // InterNetwork (IPv4) = 4 bytes, InterNetworkV6 = 16 bytes
        int Size = (Family == AddressFamily.InterNetwork) ? 4 : 16;

        return new IPAddress(Bytes.Slice(0, Size));
    }

    public string GetAddressString()
    {
        // 1. Check if it is an IPv4-mapped IPv6 address (::ffff:0:0/96)
        // High 64 bits must be 0, bits 64-95 must be 0, bits 96-111 must be 0xFFFF
        bool isIPv4Mapped = (Address >> 32) == (UInt128)0x00000000000000000000ffffB;

        if (isIPv4Mapped)
        {
            // Extract the last 32 bits for IPv4 (e.g., 192.168.1.1)
            uint ipv4Raw = (uint)(Address & 0xFFFFFFFF);
            return $"{(ipv4Raw >> 24) & 0xFF}.{(ipv4Raw >> 16) & 0xFF}.{(ipv4Raw >> 8) & 0xFF}.{ipv4Raw & 0xFF}";
        }

        // 2. Otherwise, treat as IPv6 (Standard 8 hextets)
        // We extract 16-bit chunks from the 128-bit integer
        ushort[] segments = new ushort[8];
        for (int i = 0; i < 8; i++)
        {
            // Shift right and mask to get each 16-bit block
            segments[7 - i] = (ushort)((Address >> (i * 16)) & 0xFFFF);
        }

        // Return the formatted IPv6 string
        // Note: This simple version doesn't compress zeros (::). 
        // For full RFC 5952 compliance (zero compression), IPAddress.ToString() is usually better.
        return string.Join(":", segments.Select(s => s.ToString("x")));
    }

    public string GetAddressStringZero()
    {
        // 1. Check for IPv4-mapped IPv6 (::ffff:0:0/96)
        // High 80 bits are 0, next 16 are 0xFFFF
        if ((Address >> 32) == (UInt128)0x00000000000000000000ffffu)
        {
            uint v4 = (uint)(Address & 0xFFFFFFFF);
            return $"{(v4 >> 24) & 0xFF}.{(v4 >> 16) & 0xFF}.{(v4 >> 8) & 0xFF}.{v4 & 0xFF}";
        }

        // 2. Extract the 8 16-bit segments (hextets)
        ushort[] segments = new ushort[8];
        for (int i = 0; i < 8; i++)
        {
            segments[7 - i] = (ushort)((Address >> (i * 16)) & 0xFFFF);
        }

        // 3. Find the longest run of consecutive zeros for compression ("::")
        int bestStart = -1;
        int bestLen = 0;
        int curStart = -1;
        int curLen = 0;

        for (int i = 0; i < 8; i++)
        {
            if (segments[i] == 0)
            {
                if (curStart == -1) curStart = i;
                curLen++;
                if (curLen > bestLen)
                {
                    bestStart = curStart;
                    bestLen = curLen;
                }
            }
            else
            {
                curStart = -1;
                curLen = 0;
            }
        }

        // Per RFC 5952: Only compress if the run is > 1 segment
        if (bestLen <= 1) bestStart = -1;

        // 4. Build the string
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 8; i++)
        {
            if (i == bestStart)
            {
                sb.Append("::");
                i += (bestLen - 1);
                continue;
            }

            // Add separator if not at start and previous wasn't the double colon
            if (i > 0 && sb[sb.Length - 1] != ':')
                sb.Append(':');

            sb.Append(segments[i].ToString("x"));
        }

        return sb.ToString();
    }
}