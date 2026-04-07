using Astral.Network.Connections;
using Astral.Network.Enums;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Xunit.Abstractions;

namespace Astral.Network.UnitTests.Tools;

[Collection("DisableParallelizationCollection")]
public class PacketFragmentationHandlerTests
{
    private readonly ITestOutputHelper Output;

    public PacketFragmentationHandlerTests(ITestOutputHelper Output)
    {
        //AutoParallelTickManager.Initialize();
        this.Output = Output;
    }

    void CheckPoolLeaks(bool LeaksExpected = false)
    {
        var LeaksStrList = PooledObjectsTracker.ReportLeaks();
        if (LeaksStrList != null && !LeaksExpected)
        {
            Output.WriteLine(string.Join("\n", LeaksStrList!));
            //Assert.Fail(string.Join("\n", LeaksStrList!));
            Assert.Fail($"Pool leak.");
        }
    }

    class PktFragHandlerTests_SimpleCycle_1 { }
    class PktFragHandlerTests_SimpleCycle_2 { }
    [Fact]
    public async Task SimpleCycleAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        byte[] SendBuffer = new byte[1024];
        for (int I = 0; I < 1024; I++)
            SendBuffer[I] = (byte)((I & 1) == 0 ? 1 : 0);

        var PacketOut = Conn.CreateReliablePacket<PktFragHandlerTests_SimpleCycle_1>(EProtocolMessage.Reliable);

        PacketOut.Serialize(SendBuffer);

        // 2. Fragment the packet
        var Fragments = new List<PooledOutPacket>();
        Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks);

        Assert.True(Fragments.Count > 1, "Packet should produce multiple fragments");

        // 3. Convert each fragment to PooledInPacket
        var PacketsIn = Fragments.Select(Frag =>
        {
            var Pkt = PooledInPacket.Rent<PktFragHandlerTests_SimpleCycle_2>(Frag);
            Pkt.Init();
            return Pkt;
        }).ToList();

        // 4. Process fragments via handler
        PooledInPacket? Combined = null;
        foreach (var PktIn in PacketsIn)
        {
            PktIn.Serialize<long>(); // Consume timestamp
            Combined = Handler.ProcessIncomingPacket(PktIn);
        }

        Assert.NotNull(Combined);

        // 5. Verify data integrity
        var RecvBuffer = Combined.SerializeArray<byte>();
        for (int I = 0; I < RecvBuffer.Length; I++)
        {
            byte Expected = (byte)((I & 1) == 0 ? 1 : 0);
            var Byte = RecvBuffer[I];
            if (Byte != Expected) throw new InvalidOperationException($"Buffer mismatch");
        }

        // 6. Cleanup
        foreach (var Frags in Fragments) Frags.Return();
        Combined!.Return();

        CheckPoolLeaks();
    }

    class PktFragHandlerTests_OutOfOrder_1 { }
    class PktFragHandlerTests_OutOfOrder_2 { }

    [Fact]
    public async Task OutOfOrderAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        int TotalValues = NetaConsts.BufferMaxSizeBytes / sizeof(uint) + 2; // force fragmentation
        var PacketOut = PooledOutPacket.RentReliable<PktFragHandlerTests_OutOfOrder_1>(
            Conn.NextPacketId,
            EProtocolMessage.Reliable
        );

        // Serialize UInt32 values
        for (uint i = 0; i < TotalValues; i++)
            PacketOut.Serialize(i);

        // Fragment
        var Fragments = new List<PooledOutPacket>();
        Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks);

        Assert.True(Fragments.Count > 1, "Packet should produce multiple fragments");

        // Convert each fragment to PooledInPacket
        var PacketsIn = Fragments.Select(Frag =>
        {
            var Pkt = PooledInPacket.Rent<PktFragHandlerTests_OutOfOrder_2>(Frag);
            Pkt.Init();
            return Pkt;
        }).ToList();

        // Out-of-order arrival
        PacketsIn.Reverse();

        // Feed fragments to handler
        PooledInPacket? Combined = null;
        foreach (var PktIn in PacketsIn)
        {
            PktIn.Serialize<long>(); // Consume timestamp
            Combined = Handler.ProcessIncomingPacket(PktIn);
        }

        Assert.NotNull(Combined);

        // Verify data integrity
        for (uint i = 0; i < TotalValues; i++)
        {
            uint Value = Combined!.Serialize<uint>();
            Assert.Equal(i, Value);
        }

        // Cleanup
        foreach (var F in Fragments) F.Return();
        Combined!.Return();

        CheckPoolLeaks();
    }

    // ---------------------------------------------------------
    //  SCENARIO 3: Missing Fragment
    // ---------------------------------------------------------
    class PktFragHandlerTests_MissingFragment_1 { }
    class PktFragHandlerTests_MissingFragment_2 { }

    [Fact]
    public async Task MissingFragmentAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);
        int TotalValues = NetaConsts.BufferMaxSizeBytes / sizeof(uint) + 2;

        var PacketOut = PooledOutPacket.RentReliable<PktFragHandlerTests_MissingFragment_1>(Conn.NextPacketId, EProtocolMessage.Reliable);
        for (uint i = 0; i < TotalValues; i++) PacketOut.Serialize(i);

        var Fragments = new List<PooledOutPacket>();
        Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks);
        Assert.True(Fragments.Count > 1);

        // Remove one fragment
        Fragments.RemoveAt(Fragments.Count / 2);

        var PacketsIn = Fragments.Select(f =>
        {
            var PacketIn = PooledInPacket.Rent<PktFragHandlerTests_MissingFragment_2>(f);
            PacketIn.Init();
            return PacketIn;
        }).ToList();

        PooledInPacket? Combined = null;
        foreach (var PktIn in PacketsIn)
        {
            PktIn.Serialize<long>(); // Consume timestamp
            Combined = Handler.ProcessIncomingPacket(PktIn);
        }

        Assert.Null(Combined);

        foreach (var FragOut in Fragments) FragOut.Return();
        CheckPoolLeaks(true);
    }


    // ---------------------------------------------------------
    //  SCENARIO 5: Interleaved Different Packets
    // ---------------------------------------------------------
    class PktFragHandlerTests_Interleaved_1 { }
    class PktFragHandlerTests_Interleaved_2 { }
    class PktFragHandlerTests_Interleaved_3 { }
    class PktFragHandlerTests_Interleaved_4 { }

    [Fact]
    public async Task InterleavedDifferentPacketsAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        int TotalValues = NetaConsts.BufferMaxSizeBytes / sizeof(uint) + 2;

        var PacketOut1 = PooledOutPacket.RentReliable<PktFragHandlerTests_Interleaved_1>(Conn.NextPacketId, EProtocolMessage.Reliable);
        var PacketOut2 = PooledOutPacket.RentReliable<PktFragHandlerTests_Interleaved_2>(Conn.NextPacketId, EProtocolMessage.Reliable);

        for (int i = 0; i < TotalValues; i++)
        {
            PacketOut1.Serialize((uint)i);
            PacketOut2.Serialize((uint)i + 1000);
        }

        var Fragments1 = new List<PooledOutPacket>();
        var Fragments2 = new List<PooledOutPacket>();

        Handler.ProcessOutgoingPacket(PacketOut1, Fragments1, Context.Ticks);
        Handler.ProcessOutgoingPacket(PacketOut2, Fragments2, Context.Ticks);

        Assert.True(Fragments1.Count > 1);
        Assert.True(Fragments2.Count > 1);

        var AllFragments = Fragments1.Zip(Fragments2, (f1, f2) => new[] { f1, f2 }).SelectMany(x => x).ToList();

        var Combined1Values = new List<uint>();
        var Combined2Values = new List<uint>();

        foreach (var FragOut in AllFragments)
        {
            var PktIn = PooledInPacket.Rent<PktFragHandlerTests_Interleaved_3>(FragOut);
            PktIn.Init();

            PktIn.Serialize<long>(); // Consume timestamp
            var Result = Handler.ProcessIncomingPacket(PktIn);
            if (Result != null)
            {
                for (int i = 0; i < TotalValues; i++)
                {
                    uint val = Result.Serialize<uint>();
                    if (val < 1000) Combined1Values.Add(val);
                    else Combined2Values.Add(val);
                }
                Result.Return();
            }
        }

        for (int i = 0; i < TotalValues; i++)
        {
            Assert.Equal((uint)i, Combined1Values[i]);
            Assert.Equal((uint)(i + 1000), Combined2Values[i]);
        }

        foreach (var FragOut in AllFragments) FragOut.Return();

        CheckPoolLeaks();
    }

    // ---------------------------------------------------------
    //  SCENARIO 6: Multiple Small Fragments
    // ---------------------------------------------------------
    class PktFragHandlerTests_SmallFrags_1 { }
    class PktFragHandlerTests_SmallFrags_2 { }

    [Fact]
    public async Task MultipleSmallFragmentsAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        int TotalValues = NetaConsts.BufferMaxSizeBytes / sizeof(uint) + 10;

        var PacketOut = PooledOutPacket.RentReliable<PktFragHandlerTests_SmallFrags_1>(Conn.NextPacketId, EProtocolMessage.Reliable);
        for (uint i = 0; i < TotalValues; i++) PacketOut.Serialize(i);

        var Fragments = new List<PooledOutPacket>();
        Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks);

        Assert.True(Fragments.Count > 1);

        var PacketsIn = Fragments.Select(f =>
        {
            var PacketIn = PooledInPacket.Rent<PktFragHandlerTests_SmallFrags_2>(f);
            PacketIn.Init();
            return PacketIn;
        }).ToList();

        PooledInPacket? Combined = null;
        foreach (var PktIn in PacketsIn)
        {
            PktIn.Serialize<long>(); // Consume timestamp
            Combined = Handler.ProcessIncomingPacket(PktIn);
        }

        Assert.NotNull(Combined);
        for (uint i = 0; i < TotalValues; i++) Assert.Equal(i, Combined!.Serialize<uint>());

        foreach (var FragOut in Fragments) FragOut.Return();
        Combined!.Return();
        CheckPoolLeaks();
    }

    class PktFragHandlerTests_TooLarge_1 { }

    [Fact]
    public async Task TooLargePacket_ThrowsAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        // Construct a packet guaranteed to exceed 256 fragments
        int NumValues = NetaConsts.BufferMaxSizeBytes * 250 / sizeof(uint) + 10;
        var PacketOut = PooledOutPacket.RentReliable<PktFragHandlerTests_TooLarge_1>(Conn.NextPacketId, EProtocolMessage.Reliable);
        for (uint i = 0; i < NumValues; i++) PacketOut.Serialize(i);

        var Fragments = new List<PooledOutPacket>();
        Assert.Throws<InvalidOperationException>(() => Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks));
    }



    class PktFragHandlerTests_ParallelStress_1 { }
    class PktFragHandlerTests_ParallelStress_2 { }

    [Fact]
    public async Task ParallelStressAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        int NumLoops = 50000;
        int NumValues = NetaConsts.BufferMaxSizeBytes / sizeof(uint) + 10;

        await Parallel.ForEachAsync(Enumerable.Range(0, NumLoops), async (i, ct) =>
        {
            var PacketOut = PooledOutPacket.RentReliable<PktFragHandlerTests_ParallelStress_1>(Conn.NextPacketId, EProtocolMessage.Reliable);
            for (uint v = 0; v < NumValues; v++) PacketOut.Serialize(v + (uint)i * 1000);

            var Fragments = new List<PooledOutPacket>();
            Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks);

            var PacketsIn = Fragments.Select(f =>
            {
                var PacketIn = PooledInPacket.Rent<PktFragHandlerTests_ParallelStress_2>(f);
                PacketIn.Init();
                return PacketIn;
            }).ToList();

            PooledInPacket? Combined = null;
            foreach (var PktIn in PacketsIn)
            {
                PktIn.Serialize<long>(); // Consume timestamp
                Combined = Handler.ProcessIncomingPacket(PktIn);
            }

            Assert.NotNull(Combined);
            for (uint v = 0; v < NumValues; v++)
                Assert.Equal(v + (uint)i * 1000, Combined!.Serialize<uint>());

            foreach (var FragOut in Fragments) FragOut.Return();
            Combined!.Return();
        });

        CheckPoolLeaks();
    }


    class PktFragHandlerTests_ParallelOutOfOrder_1 { }
    class PktFragHandlerTests_ParallelOutOfOrder_2 { }

    [Fact]
    public async Task ParallelOutOfOrderAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        int NumClients = 10000;
        int NumValues = NetaConsts.BufferMaxSizeBytes / sizeof(uint) + 10;

        await Parallel.ForEachAsync(Enumerable.Range(0, NumClients), async (i, ct) =>
        {
            var PacketOut = PooledOutPacket.RentReliable<PktFragHandlerTests_ParallelOutOfOrder_1>(Conn.NextPacketId, EProtocolMessage.Reliable);
            for (uint v = 0; v < NumValues; v++) PacketOut.Serialize(v + (uint)i * 1000);

            var Fragments = new List<PooledOutPacket>();
            Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks);

            // Shuffle fragments
            var Random = new Random(i);
            var Shuffled = Fragments.OrderBy(_ => Random.Next()).ToList();

            var PacketsIn = Shuffled.Select(f =>
            {
                var PacketIn = PooledInPacket.Rent<PktFragHandlerTests_ParallelOutOfOrder_2>(f);
                PacketIn.Init();
                return PacketIn;
            }).ToList();

            PooledInPacket? Combined = null;
            foreach (var PktIn in PacketsIn)
            {
                PktIn.Serialize<long>(); // Consume tiemstamp
                var Result = Handler.ProcessIncomingPacket(PktIn);
                if (Result != null) Combined = Result;
            }

            Assert.NotNull(Combined);
            for (uint v = 0; v < NumValues; v++)
                Assert.Equal(v + (uint)i * 1000, Combined!.Serialize<uint>());

            foreach (var FragOut in Fragments) FragOut.Return();
            Combined!.Return();
        });

        CheckPoolLeaks();
    }


    class PktFragHandlerTests_SequentialBurst_1 { }
    class PktFragHandlerTests_SequentialBurst_2 { }

    [Fact]
    public async Task SequentialBurstAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        int NumPackets = 100; // keep reasonable for test runtime
        int NumValues = NetaConsts.BufferMaxSizeBytes / sizeof(uint) + 10; // ensure packet > BufferMaxSizeBytes

        for (int i = 0; i < NumPackets; i++)
        {
            var PacketOut = PooledOutPacket.RentReliable<PktFragHandlerTests_SequentialBurst_1>(Conn.NextPacketId, EProtocolMessage.Reliable);

            // Fill it with enough data to exceed BufferMaxSizeBytes
            for (uint v = 0; v < NumValues; v++)
                PacketOut.Serialize(v + (uint)i * 1000);

            var Fragments = new List<PooledOutPacket>();
            Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks); // now guaranteed to fragment

            // Rent incoming packets for each fragment
            var PacketsIn = Fragments.Select(f =>
            {
                var PktIn = PooledInPacket.Rent<PktFragHandlerTests_SequentialBurst_2>(f);
                PktIn.Init();
                return PktIn;
            }).ToList();

            PooledInPacket? Combined = null;
            foreach (var PktIn in PacketsIn)
            {
                PktIn.Serialize<long>(); // Consume timestamp
                Combined = Handler.ProcessIncomingPacket(PktIn);
            }

            Assert.NotNull(Combined);

            // Validate all values
            for (uint v = 0; v < NumValues; v++)
                Assert.Equal(v + (uint)i * 1000, Combined!.Serialize<uint>());

            // Return all fragments and combined packet
            foreach (var FragOut in Fragments) FragOut.Return();
            Combined!.Return();
        }

        CheckPoolLeaks();
    }

    class PktFragHandlerTests_InterleavedStress_1 { }
    class PktFragHandlerTests_InterleavedStress_2 { }

    [Fact]
    public async Task InterleavedParallelStressMissingFragsAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        int NumStreams = 1024;
        int NumValues = NetaConsts.BufferMaxSizeBytes / sizeof(uint) + 20;

        await Parallel.ForEachAsync(Enumerable.Range(0, NumStreams), async (i, ct) =>
        {
            var PacketOut = PooledOutPacket.RentReliable<PktFragHandlerTests_InterleavedStress_1>(Conn.NextPacketId, EProtocolMessage.Reliable);
            for (uint v = 0; v < NumValues; v++) PacketOut.Serialize(v + (uint)i * 1000);

            var Fragments = new List<PooledOutPacket>();
            Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks);

            // Shuffle and remove a fragment occasionally
            var Random = new Random(i);
            Shuffle(Fragments, Random.Shared);
            if (i % 50 == 0 && Fragments.Count > 1) Fragments.RemoveAt(1);

            var PacketsIn = Fragments.Select(f =>
            {
                var PacketIn = PooledInPacket.Rent<PktFragHandlerTests_InterleavedStress_2>(f);
                PacketIn.Init();
                return PacketIn;
            }).ToList();

            PooledInPacket? Combined = null;
            foreach (var PktIn in PacketsIn)
            {
                PktIn.Serialize<ulong>(); // Consume timestamp
                var Result = Handler.ProcessIncomingPacket(PktIn);
                if (Result != null) Combined = Result;
            }

            if (i % 50 != 0)
            {
                Assert.NotNull(Combined);
                for (uint v = 0; v < NumValues; v++)
                    Assert.Equal(v + (uint)i * 1000, Combined!.Serialize<uint>());
            }
            else
            {
                // Missing fragment → may be null
                Assert.Null(Combined);
            }

            foreach (var FragOut in Fragments) FragOut.Return();
            Combined?.Return();
        });

        CheckPoolLeaks(true);
    }


    [Fact]
    public async Task InterleavedParallelStressNoMissingFragsAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        var Conn = new NetaConnection();
        var Handler = new PacketFragmentationHandler(Conn);

        int NumStreams = 1024;

        int PayloadSize = NetaConsts.BufferMaxSizeBytes;
        byte[] SendBuffer = new byte[PayloadSize];
        for (int I = 0; I < PayloadSize; I++)
            SendBuffer[I] = (byte)((I & 1) == 0 ? 1 : 0);

        await Parallel.ForEachAsync(Enumerable.Range(0, NumStreams), async (i, ct) =>
        {
            // Create outgoing packet and serialize values
            var PacketOut = PooledOutPacket.RentReliable<PktFragHandlerTests_InterleavedStress_1>(Conn.NextPacketId, EProtocolMessage.Reliable);
            PacketOut.Serialize(SendBuffer);

            // Fragment packet
            var Fragments = new List<PooledOutPacket>();
            Handler.ProcessOutgoingPacket(PacketOut, Fragments, Context.Ticks);

            Shuffle(Fragments, Random.Shared);

            // Convert to incoming packets
            var PacketsIn = Fragments.Select(f =>
            {
                var PacketIn = PooledInPacket.Rent<PktFragHandlerTests_InterleavedStress_2>(f);
                PacketIn.Init();

                Assert.True((PacketIn.Flags & EPacketFlags.Reliable) != 0);
                PacketIn.Serialize<ulong>(); // Consume timestamp
                return PacketIn;
            }).ToList();

            // Process incoming fragments
            PooledInPacket? Combined = null;
            foreach (var PktIn in PacketsIn)
            {
                var Result = Handler.ProcessIncomingPacket(PktIn);
                if (Result != null)
                {
                    Combined = Result;
                    break;
                }
            }

            // All fragments present → Combined should never be null
            Assert.NotNull(Combined);
            var RecvBuffer = Combined.SerializeArray<byte>();
            for (int I = 0; I < RecvBuffer.Length; I++)
            {
                byte Expected = (byte)((I & 1) == 0 ? 1 : 0);
                var Byte = RecvBuffer[I];
                if (Byte != Expected) throw new InvalidOperationException($"Buffer mismatch");
            }

            // Return outgoing fragments and combined packet
            foreach (var FragOut in Fragments) FragOut.Return();
            Combined?.Return();
        });

        CheckPoolLeaks();
    }

    static void Shuffle<T>(IList<T> List, Random Rng)
    {
        for (int I = List.Count - 1; I > 0; I--)
        {
            int J = Rng.Next(0, I + 1);
            (List[I], List[J]) = (List[J], List[I]);
        }
    }
}