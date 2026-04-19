using Astral.Network.Sockets;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Astral.Network.Servers.NetaServer;

namespace Astral.Network.Toolkit;

public readonly struct NetaAddress : IEquatable<NetaAddress>
{
    public readonly UInt128 Address;
    public readonly ushort Port;
    public readonly AddressFamily Family; // We need this to know the original "size"
    public readonly uint Hash;


    public NetaAddress(UInt128 Address, ushort Port)
    {
        this.Address = Address;
        this.Port = Port;

        Hash = (uint)(Address >> 96) ^ (uint)(Address >> 64) ^ (uint)(Address >> 32) ^ (uint)Address ^ Port;
    }
    public NetaAddress(IPEndPoint EP)
    {
        Port = (ushort)EP.Port;
        Family = EP.Address.AddressFamily;

        Span<byte> bytes = stackalloc byte[16];
        if (EP.Address.TryWriteBytes(bytes, out int written))
        {
            if (Family == AddressFamily.InterNetwork) // IPv4
            {
                // Read ONLY the 4 bytes into a uint, then cast to UInt128
                // This places 127.0.0.1 at the BOTTOM of the UInt128
                Address = (UInt128)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(0, 4));
            }
            else // IPv6 (Native or Mapped)
            {
                // Check for mapping even if the Family says V6
                if (EP.Address.IsIPv4MappedToIPv6)
                {
                    // Extract just the IPv4 part to keep the key consistent
                    Address = (UInt128)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(12, 4));
                }
                else
                {
                    // Full 128-bit native IPv6
                    Address = BinaryPrimitives.ReadUInt128BigEndian(bytes);
                }
            }
        }
        else
        {
            Address = 0;
        }

        Hash = (uint)(Address >> 96) ^ (uint)(Address >> 64) ^ (uint)(Address >> 32) ^ (uint)Address ^ Port;
    }

#if LINUX
    unsafe public NetaAddress(ref SocketAddrStorage Storage)
    {
        Port = (ushort)IPAddress.NetworkToHostOrder((short)Storage.sin_port);
        Span<byte> buffer = stackalloc byte[16];

        if (Storage.ss_family == 2)
        {
            // Write IPv4 to the FRONT so Slice(0, 4) works
            BinaryPrimitives.WriteUInt32BigEndian(buffer, Storage.sin_addr);
        }
        else
        {
            // Copy the 16 bytes exactly as they are in the struct
            fixed (ulong* p = &Storage.sin6_addr_hi)
            {
                new ReadOnlySpan<byte>(p, 16).CopyTo(buffer);
            }
        }

        // Read it as Big Endian so WriteUInt128BigEndian puts it back exactly the same
        Address = BinaryPrimitives.ReadUInt128BigEndian(buffer);
        Hash = (uint)(Address >> 96) ^ (uint)(Address >> 64) ^ (uint)(Address >> 32) ^ (uint)Address ^ Port;
    } 
#endif

    public unsafe NetaAddress(SocketAddress sa)
    {
        // 1. Extract Port (Offset 2, Big-Endian)
        // Manual bit-shifting to avoid any array allocations
        Port = (ushort)((sa[2] << 8) | sa[3]);

        // 2. Extract Address (Offset 8)
        // We read as two ulongs to construct a stable UInt128
        ReadOnlySpan<byte> addrSpan = stackalloc byte[16];
        for (int i = 0; i < 16; i++)
        {
            ((byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(addrSpan)))[i] = sa[i + 8];
        }

        // Handle IPv4-mapped IPv6 (00...00FFFF:xxxx:xxxx)
        if (IsIPv4Mapped(addrSpan))
        {
            // Extract only the 4-byte IPv4 part into the lower bits of the UInt128
            uint ipv4 = BinaryPrimitives.ReadUInt32BigEndian(addrSpan.Slice(12));
            Address = (UInt128)ipv4;
        }
        else
        {
            // Native IPv6: Read as two 64-bit halves for a stable UInt128
            ulong upper = BinaryPrimitives.ReadUInt64BigEndian(addrSpan.Slice(0, 8));
            ulong lower = BinaryPrimitives.ReadUInt64BigEndian(addrSpan.Slice(8, 8));
            Address = new UInt128(upper, lower);
        }

        Hash = (uint)(Address >> 96) ^ (uint)(Address >> 64) ^ (uint)(Address >> 32) ^ (uint)Address ^ Port;
    }

    private static bool IsIPv4Mapped(ReadOnlySpan<byte> span)
    {
        // Check for 10 bytes of 0 and 2 bytes of 0xFF
        return span[10] == 0xFF && span[11] == 0xFF &&
               span[0] == 0 && span[1] == 0 && span[2] == 0 && span[3] == 0 &&
               span[4] == 0 && span[5] == 0 && span[6] == 0 && span[7] == 0 &&
               span[8] == 0 && span[9] == 0;
    }

    public bool Equals(NetaAddress Other) => Port == Other!.Port && Address == Other.Address;
    public override bool Equals(object? Obj) => Obj is NetaAddress k && Equals(k);
    public override int GetHashCode() => (int)Hash;
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
        // Check if the address is normalized IPv4 (lower 32 bits only)
        // OR if the Family is explicitly InterNetwork
        if (Family == AddressFamily.InterNetwork || Address <= uint.MaxValue)
        {
            uint ipv4 = (uint)Address;

            // We MUST use the 4-byte constructor to get "127.0.0.1"
            // If we use the 16-byte constructor, we get "[::127.0.0.1]"
            Span<byte> v4Bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(v4Bytes, ipv4);

            return new IPAddress(v4Bytes);
        }

        // Real IPv6
        Span<byte> v6Bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128BigEndian(v6Bytes, Address);
        return new IPAddress(v6Bytes);
    }

    public string GetAddressString()
    {
        ulong high = (ulong)(Address >> 64);
        ulong low = (ulong)(Address & ulong.MaxValue);

        // 1. Detect IPv4-Mapped (::ffff:127.0.0.1) or Normalized IPv4 (Address <= uint.MaxValue)
        // Most dual-stack sockets expect the ::ffff: prefix to connect to IPv4 via IPv6
        bool isMapped = (high == 0 && (low >> 32) == 0x0000FFFF);
        bool isNormalized = (Address <= uint.MaxValue);

        if (isMapped || isNormalized)
        {
            uint ipv4Raw = (uint)(low & 0xFFFFFFFF);
            // Extract octets assuming Big-Endian/Network Order
            return $"{(ipv4Raw >> 24) & 0xFF}.{(ipv4Raw >> 16) & 0xFF}.{(ipv4Raw >> 8) & 0xFF}.{ipv4Raw & 0xFF}";
        }

        // 2. Real IPv6 - Use stackalloc to avoid string.Join allocations
        // We format as a standard IPv6 string.
        return string.Format("{0:x}:{1:x}:{2:x}:{3:x}:{4:x}:{5:x}:{6:x}:{7:x}",
            (ushort)(Address >> 112),
            (ushort)(Address >> 96),
            (ushort)(Address >> 80),
            (ushort)(Address >> 64),
            (ushort)(Address >> 48),
            (ushort)(Address >> 32),
            (ushort)(Address >> 16),
            (ushort)(Address & 0xFFFF));
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




    public SocketAddress ToSocketAddress()
    {
        // 1. Create the SocketAddress container for IPv6 (Size 28)
        // This is the only allocation, which is necessary to create the object.
        var sa = new SocketAddress(AddressFamily.InterNetworkV6, 28);

        // 2. Set Port (Offset 2, Big-Endian)
        sa[2] = (byte)(Port >> 8);
        sa[3] = (byte)(Port & 0xFF);

        // 3. Set Address (Offset 8)
        // If the address fits in a uint32, we assume it's an IPv4-mapped address
        if (Address <= uint.MaxValue)
        {
            // Set the IPv4-mapped prefix: 00...00FFFF
            for (int i = 0; i < 10; i++) sa[i + 8] = 0;
            sa[18] = 0xFF;
            sa[19] = 0xFF;

            // Set the 4-byte IPv4 address (Big-Endian)
            uint ipv4 = (uint)Address;
            sa[20] = (byte)(ipv4 >> 24);
            sa[21] = (byte)(ipv4 >> 16);
            sa[22] = (byte)(ipv4 >> 8);
            sa[23] = (byte)(ipv4 & 0xFF);
        }
        else
        {
            // Standard IPv6: Extract from UInt128
            // We need to reverse the logic used in the EndPointKey ctor
            for (int i = 0; i < 16; i++)
            {
                // Extracting bytes from UInt128 (Big-Endian order)
                // Shift by (15, 14, ... 0) * 8 bits
                sa[i + 8] = (byte)(Address >> ((15 - i) * 8));
            }
        }

        return sa;
    }
}