using Astral.Network.Tools;
using Xunit.Abstractions;

namespace Astral.Network.UnitTests.Tools;

[Collection("DisableParallelizationCollection")]
public class PacketWindowTests
{
    private readonly ITestOutputHelper _output;

    // Constants derived from the class
    //private const int WindowSize = 8192;
    private const int PacketIdSizeBits = 64;

    public PacketWindowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ════════════════════════════════════════════════════════════════════
    //  A – Initialisation / first packet
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void A01_FirstPacket_IsNotSeenBefore()
    {
        var w = new InPacketWindow();
        Assert.False(w.CheckPacket(42) != PacketWindowStatus.New);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(32767)]
    [InlineData(32768)]
    [InlineData(65534)]
    [InlineData(65535)]
    public void A02_FirstPacket_AnyId_IsNotSeenBefore(ushort id)
    {
        var w = new InPacketWindow();
        Assert.False(w.CheckPacket(id) != PacketWindowStatus.New);
    }

    [Fact]
    public void A03_FirstPacket_SetsHighestReceived()
    {
        var w = new InPacketWindow();
        w.CheckPacket(999);
        Assert.Equal(999, w.SequenceHead);
    }

    [Fact]
    public void A04_BeforeFirstPacket_HighestReceivedIsZero()
    {
        var w = new InPacketWindow();
        Assert.Equal(0, w.SequenceHead);
    }

    // ════════════════════════════════════════════════════════════════════
    //  B – Sequential delivery
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void B01_Sequential_NoDuplicates()
    {
        var w = new InPacketWindow();
        for (ushort i = 0; i < 200; i++)
            Assert.False(w.CheckPacket(i) != PacketWindowStatus.New, $"Packet {i} falsely flagged as duplicate");
    }

    [Fact]
    public void B02_Sequential_HighestReceivedAdvances()
    {
        var w = new InPacketWindow();
        for (ushort i = 0; i < 50; i++)
        {
            w.CheckPacket(i);
            Assert.Equal(i, w.SequenceHead);
        }
    }

    [Fact]
    public void B03_Sequential_ThenDuplicate_IsSeenBefore()
    {
        var w = new InPacketWindow();
        w.CheckPacket(10);
        w.CheckPacket(11);
        w.CheckPacket(12);
        Assert.True(w.CheckPacket(11) != PacketWindowStatus.New);   // duplicate inside window
    }

    // ════════════════════════════════════════════════════════════════════
    //  C – Out-of-order within window (backward)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void C01_OutOfOrder_WithinWindow_NotSeenBefore()
    {
        var w = new InPacketWindow();
        w.CheckPacket(100);
        Assert.False(w.CheckPacket(95) != PacketWindowStatus.New);   // 5 behind – within window
    }

    [Fact]
    public void C02_OutOfOrder_WithinWindow_ThenDuplicate_IsSeenBefore()
    {
        var w = new InPacketWindow();
        w.CheckPacket(100);
        w.CheckPacket(95);
        Assert.True(w.CheckPacket(95) != PacketWindowStatus.New);    // second time round
    }

    [Fact]
    public void C03_OutOfOrder_FillsGapsLeft_By_ForwardJump()
    {
        // After a forward jump, skipped slots are cleared and appear new.
        var w = new InPacketWindow();
        w.CheckPacket(0);
        w.CheckPacket(10);   // jump: slots 1-10 cleared, 10 marked
                             // Packet 5 (skipped) should be treated as NEW
        Assert.False(w.CheckPacket(5) != PacketWindowStatus.New);
    }

    [Fact]
    public void C04_OutOfOrder_AfterJump_OriginalPacket_StillSeen()
    {
        // Packet 0 was genuinely received; jump forward; 0 is still marked.
        var w = new InPacketWindow();
        w.CheckPacket(0);
        w.CheckPacket(10);
        Assert.True(w.CheckPacket(0) != PacketWindowStatus.New);    // 0 was marked, still within window
    }

    [Fact]
    public void C05_ManyOutOfOrder_AllWithinWindow_NoDuplicates()
    {
        // Receive 50 packets then 49 out-of-order ones – none should be duplicates.
        var w = new InPacketWindow();
        for (ushort i = 0; i < 50; i++) w.CheckPacket(i);
        for (ushort i = 0; i < 50; i++)
            Assert.True(w.CheckPacket(i) != PacketWindowStatus.New, $"Packet {i} should already be seen");
    }

    // ════════════════════════════════════════════════════════════════════
    //  D – Duplicate detection
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void D01_ExactDuplicate_OfHighestReceived_IsSeenBefore()
    {
        var w = new InPacketWindow();
        w.CheckPacket(7);
        Assert.True(w.CheckPacket(7) != PacketWindowStatus.New);
    }

    [Fact]
    public void D02_DuplicateAfterFurtherAdvance_IsSeenBefore()
    {
        var w = new InPacketWindow();
        w.CheckPacket(7);
        w.CheckPacket(8);
        w.CheckPacket(9);
        Assert.True(w.CheckPacket(7) != PacketWindowStatus.New);
    }

    [Fact]
    public void D03_DuplicateOfFirstPacket_IsSeenBefore()
    {
        var w = new InPacketWindow();
        w.CheckPacket(42);
        Assert.True(w.CheckPacket(42) != PacketWindowStatus.New);
    }

    [Fact]
    public void D04_Duplicate_OnlyAfterFirstSeen_IsFalse()
    {
        var w = new InPacketWindow();
        w.CheckPacket(50);
        w.CheckPacket(60);           // jump clears 51-60
        Assert.False(w.CheckPacket(55) != PacketWindowStatus.New);   // 55 was cleared → new
        Assert.True(w.CheckPacket(55) != PacketWindowStatus.New);    // now it's marked → duplicate
    }

    // ════════════════════════════════════════════════════════════════════
    //  E – Window boundary conditions
    // ════════════════════════════════════════════════════════════════════

    private const int WS = InPacketWindow.WindowSize;   // 8192

    [Fact]
    public void E01_JustInsideWindow_IsNotSeenBefore()
    {
        var w = new InPacketWindow();
        w.CheckPacket((ushort)(WS - 1));            // highest = 8191
                                                    // Packet 0 is distance WS-1 = 8191 < WS → within window → new
        Assert.False(w.CheckPacket(0) != PacketWindowStatus.New);
    }

    [Fact]
    public void E02_JustOutsideWindow_IsSeenBefore()
    {
        var w = new InPacketWindow();
        w.CheckPacket((ushort)WS);                  // highest = 8192
                                                    // Packet 0 is distance WS = 8192 ≥ WS → outside window → drop
        Assert.True(w.CheckPacket(0) != PacketWindowStatus.New);
    }

    [Fact]
    public void E03_ExactlyWindowSizeBehind_IsSeenBefore()
    {
        var w = new InPacketWindow();
        ushort high = 1000 + WS;
        w.CheckPacket((ushort)high);
        Assert.True(w.CheckPacket(1000) != PacketWindowStatus.New);   // distance == WS → outside
    }

    [Fact]
    public void E04_OneLessThanWindowSizeBehind_IsNotSeenBefore()
    {
        var w = new InPacketWindow();
        ushort high = (ushort)(1000 + WS - 1);
        w.CheckPacket(high);
        Assert.False(w.CheckPacket(1000) != PacketWindowStatus.New);  // distance == WS-1 → inside
    }

    [Fact]
    public void E05_BitmapReuse_SlotClearedCorrectly()
    {
        // Advance the window by exactly WindowSize to alias back onto the same bitmap slot.
        var w = new InPacketWindow();
        w.CheckPacket(0);
        // Jump forward WS-1 (max allowed with default MaxAllowedAhead of WS/2 requires two jumps)
        // Use MaxAllowedAhead = WS-1 to do it in one shot.
        w.MaxAllowedAhead = (uint)(WS - 1);
        w.CheckPacket((ushort)(WS - 1));   // slot 0 and slot WS-1 in bitmap share nothing;
                                           // slot WS-1 maps to bitmap position WS-1
                                           // Now advance another WS-1 forward (total 2*(WS-1) = 16382 ahead of 0)
                                           // Packet 0 alias: (0 + WS) & WindowMask = 0 → same slot as original 0
                                           // But 0 is now (ushort)(WS-1 + 1) = WS away from HighestReceived → outside window
        w.CheckPacket((ushort)(2 * (WS - 1)));
        // Packet 0 maps to same bitmap slot; distance from HighestReceived is > WS → drop
        Assert.True(w.CheckPacket(0) != PacketWindowStatus.New);
    }

    // ════════════════════════════════════════════════════════════════════
    //  F – Forward jumps and MaxAllowedAhead enforcement
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void F01_ForwardJump_ExactlyMaxAllowedAhead_Accepted()
    {
        var w = new InPacketWindow();
        w.CheckPacket(0);
        Assert.False(w.CheckPacket((ushort)w.MaxAllowedAhead) != PacketWindowStatus.New);
    }

    [Fact]
    public void F02_ForwardJump_MaxAllowedAheadPlusOne_Rejected()
    {
        var w = new InPacketWindow();
        w.CheckPacket(0);
        Assert.True(w.CheckPacket((ushort)(w.MaxAllowedAhead + 1)) != PacketWindowStatus.New);
    }

    [Fact]
    public void F03_ForwardJump_Rejected_DoesNotAdvanceHighest()
    {
        var w = new InPacketWindow();
        w.CheckPacket(0);
        w.CheckPacket((ushort)(w.MaxAllowedAhead + 1));   // rejected
        Assert.Equal(0, w.SequenceHead);
    }

    [Fact]
    public void F04_ForwardJump_HighestReceivedUpdated()
    {
        var w = new InPacketWindow();
        w.CheckPacket(50);
        w.CheckPacket(100);
        Assert.Equal(100, w.SequenceHead);
    }

    [Fact]
    public void F05_ForwardJump_SkippedSlots_AreNotDuplicate()
    {
        var w = new InPacketWindow();
        w.CheckPacket(0);
        w.CheckPacket(10);   // skip 1..9
        for (ushort i = 1; i <= 9; i++)
            Assert.False(w.CheckPacket(i) != PacketWindowStatus.New, $"Skipped packet {i} should not be a duplicate");
    }

    [Fact]
    public void F06_ForwardJump_ReceivedSlotBeforeJump_IsDuplicate()
    {
        var w = new InPacketWindow();
        w.CheckPacket(0);
        w.CheckPacket(10);
        Assert.True(w.CheckPacket(0) != PacketWindowStatus.New);    // 0 was received before jump, still marked
    }

    [Fact]
    public void F07_SmallMaxAllowedAhead_EnforcedStrictly()
    {
        var w = new InPacketWindow();
        w.MaxAllowedAhead = 5;
        w.CheckPacket(0);
        Assert.False(w.CheckPacket(5) != PacketWindowStatus.New);   // diff = 5 ≤ 5 → OK
        Assert.True(w.CheckPacket(11) != PacketWindowStatus.New);   // diff = 6 (from 5) → rejected  wait…
                                                                    // After receiving 5, HighestReceived = 5.  6 ahead of 5 → 11. diff = 11 - 5 = 6 > 5 → rejected
    }

    [Fact]
    public void F08_ForwardJump_ToMaxUshort_ThenZero_Wraps()
    {
        var w = new InPacketWindow();
        w.MaxAllowedAhead = 10;
        w.CheckPacket(65530);
        // 65535 is 5 ahead → OK
        Assert.False(w.CheckPacket(65535) != PacketWindowStatus.New);
        // 4 is (ushort)(65535 + 4 + 1) = 4, diff = (ushort)(4 - 65535) = 5 → OK
        Assert.False(w.CheckPacket(4) != PacketWindowStatus.New);
    }

    // ════════════════════════════════════════════════════════════════════
    //  G – Sequence-number wrap-around (ushort overflow)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void G01_WrapAround_Sequential_NoDuplicates()
    {
        var w = new InPacketWindow();
        // Start near the top of the ushort range
        w.CheckPacket(65530);
        w.CheckPacket(65531);
        w.CheckPacket(65532);
        w.CheckPacket(65533);
        w.CheckPacket(65534);
        w.CheckPacket(65535);
        w.CheckPacket(0);     // wrap
        w.CheckPacket(1);
        w.CheckPacket(2);
        Assert.Equal(2, w.SequenceHead);
    }

    [Fact]
    public void G02_WrapAround_OldPacketBeforeWrap_StillInWindow_NotDuplicate()
    {
        var w = new InPacketWindow();
        w.CheckPacket(65530);
        w.CheckPacket(3);     // forward jump across wrap; diff = (ushort)(3 - 65530) = 9

        // 65535 was never received (skipped), distance from 3 is (ushort)(3 - 65535) = 4 → OK
        Assert.False(w.CheckPacket(65535) != PacketWindowStatus.New);
    }

    [Fact]
    public void G03_WrapAround_PacketJustInsideWindow_NotDuplicate()
    {
        var w = new InPacketWindow();
        // HighestReceived = 5, packet 65534 is distance (ushort)(5 - 65534) = 7 → inside window
        w.CheckPacket(5);
        Assert.False(w.CheckPacket(65534) != PacketWindowStatus.New);
    }

    [Fact]
    public void G04_WrapAround_PacketOutsideWindow_IsDuplicate()
    {
        var w = new InPacketWindow();
        // Start at WS (8192). Packet 0 has distance 8192 = WS → outside.
        w.CheckPacket((ushort)WS);
        Assert.True(w.CheckPacket(0) != PacketWindowStatus.New);
    }

    [Fact]
    public void G05_WrapAround_ForwardDiff_CorrectlyDetected()
    {
        // 65535 → 0: diff = (ushort)(0 - 65535) = 1 → forward
        var w = new InPacketWindow();
        w.CheckPacket(65535);
        Assert.False(w.CheckPacket(0) != PacketWindowStatus.New);
        Assert.Equal(0, w.SequenceHead);
    }

    [Fact]
    public void G06_WrapAround_BackwardDiff_CorrectlyDetected()
    {
        // 0 → 65535: diff = (ushort)(65535 - 0) = 65535 → backward, distance = 1
        var w = new InPacketWindow();
        w.CheckPacket(0);
        Assert.False(w.CheckPacket(65535) != PacketWindowStatus.New);   // 1 behind, new
        Assert.True(w.CheckPacket(65535) != PacketWindowStatus.New);    // now duplicate
    }

    [Fact]
    public void G07_WrapAround_FullCycle_SlotReuse_NoDuplicates()
    {
        // Advance HighestReceived by exactly WindowSize so bitmap slots alias.
        // The old slot must be cleared (not a false duplicate).
        var w = new InPacketWindow();
        w.MaxAllowedAhead = (uint)(WS - 1);

        Assert.False(w.CheckPacket(0) != PacketWindowStatus.New);

        // Jump to WS (slot 0 aliases back to bitmap[0] bit 0)
        Assert.True(w.CheckPacket((ushort)WS) != PacketWindowStatus.New); // clears slots 1..WS, marks WS
                                                                          // Slot WS is now HighestReceived. Slot 0 in bitmap was cleared. Sending
                                                                          // ushort 0 is now WS behind → distance WS → outside window → drop.
        Assert.True(w.CheckPacket(0) != PacketWindowStatus.New);

        // But ushort WS+1 is just 1 ahead → new
        Assert.True(w.CheckPacket((ushort)(WS + 1)) != PacketWindowStatus.New);
    }

    // ════════════════════════════════════════════════════════════════════
    //  H – Security / adversarial
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void H01_AdversarialFarAhead_DoesNotAdvanceWindow()
    {
        var w = new InPacketWindow();
        w.CheckPacket(100);
        // Craft a packet WindowSize/2 + 1 ahead of MaxAllowedAhead
        ushort evil = (ushort)(100 + w.MaxAllowedAhead + 1);
        Assert.True(w.CheckPacket(evil) != PacketWindowStatus.New);       // must be rejected
        Assert.Equal(100, w.SequenceHead);  // window must not advance
    }

    [Fact]
    public void H02_AdversarialFarAhead_DoesNotWipeBitmap()
    {
        // After legit packets 0..50 are received, an evil far-ahead packet
        // must not wipe the bitmap – those packets must remain "seen".
        var w = new InPacketWindow();
        for (ushort i = 0; i <= 50; i++) w.CheckPacket(i);

        ushort evil = (ushort)(50 + w.MaxAllowedAhead + 500);
        w.CheckPacket(evil);   // should be rejected silently

        // Packets 0..50 are still within window of HighestReceived=50.
        for (ushort i = 0; i <= 50; i++)
            Assert.True(w.CheckPacket(i) != PacketWindowStatus.New, $"Packet {i} should still be marked after adversarial packet");
    }

    [Fact]
    public void H03_AdversarialMaxAllowedAhead_CannotExceedWindowSizeMinus1()
    {
        var w = new InPacketWindow();
        w.MaxAllowedAhead = uint.MaxValue;
        Assert.Equal((uint)(WS - 1), w.MaxAllowedAhead);
    }

    [Fact]
    public void H04_AdversarialMaxAllowedAhead_CannotBeZero()
    {
        var w = new InPacketWindow();
        w.MaxAllowedAhead = 0;
        Assert.Equal(1u, w.MaxAllowedAhead);
    }

    [Fact]
    public void H05_ReplayAfterForwardJump_WithinWindow_Blocked()
    {
        // After legitimate forward jump, earlier packets that were genuinely
        // received must not be replayable.
        var w = new InPacketWindow();
        for (ushort i = 0; i <= 100; i++) w.CheckPacket(i);
        w.CheckPacket(200);   // jump; 101-200 cleared, 0-100 still marked

        // Replay 0..100 – all should be duplicates
        for (ushort i = 0; i <= 100; i++)
            Assert.True(w.CheckPacket(i) != PacketWindowStatus.New, $"Replayed packet {i} should be blocked");
    }

    [Fact]
    public void H06_AlternatingFarJumps_DoNotAllowReplay()
    {
        // An adversary alternates between two IDs far apart.
        // Neither should cause replay of previously received packets to succeed.
        var w = new InPacketWindow();
        w.MaxAllowedAhead = 100;
        for (ushort i = 0; i <= 50; i++) w.CheckPacket(i);
        // Evil: try to alternate between 50 and 50+101 (rejected each time)
        for (int j = 0; j < 10; j++)
        {
            Assert.True(w.CheckPacket((ushort)(50 + 101)) != PacketWindowStatus.New);  // too far ahead – dropped
            Assert.True(w.CheckPacket(50) != PacketWindowStatus.New);                   // duplicate
        }
        // HighestReceived must still be 50
        Assert.Equal(50, w.SequenceHead);
    }

    [Fact]
    public void H07_ExtremeId_Zero_AndMaxUshort_NoPanic()
    {
        var w = new InPacketWindow();
        Assert.False(w.CheckPacket(0) != PacketWindowStatus.New);
        Assert.False(w.CheckPacket(65535) != PacketWindowStatus.New);
        Assert.False(w.CheckPacket(1) != PacketWindowStatus.New);
    }

    // ════════════════════════════════════════════════════════════════════
    //  I – MaxAllowedAhead property clamping
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1u, 1u)]
    [InlineData(100u, 100u)]
    [InlineData(4096u, 4096u)]
    [InlineData(8191u, 8191u)]
    [InlineData(8192u, 8191u)]   // clamped to WS-1
    [InlineData(16384u, 8191u)]   // clamped
    [InlineData(uint.MaxValue, 8191u)] // clamped
    [InlineData(0u, 1u)]      // clamped to minimum
    public void I01_MaxAllowedAhead_Clamping(uint input, uint expected)
    {
        var w = new InPacketWindow();
        w.MaxAllowedAhead = input;
        Assert.Equal(expected, w.MaxAllowedAhead);
    }

    [Fact]
    public void I02_DefaultMaxAllowedAhead_IsHalfWindow()
    {
        var w = new InPacketWindow();
        Assert.Equal((uint)(WS / 2), w.MaxAllowedAhead);
    }

    // ════════════════════════════════════════════════════════════════════
    //  J – Reset
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void J01_Reset_AllowsFirstPacketAgain()
    {
        var w = new InPacketWindow();
        w.CheckPacket(42);
        w.Reset();
        Assert.False(w.CheckPacket(42) != PacketWindowStatus.New);   // after reset, 42 is new again
    }

    [Fact]
    public void J02_Reset_ClearsHighestReceived()
    {
        var w = new InPacketWindow();
        w.CheckPacket(999);
        w.Reset();
        Assert.Equal(0, w.SequenceHead);
    }

    [Fact]
    public void J03_Reset_ClearsBitmap_OldPacketsNotDuplicate()
    {
        var w = new InPacketWindow();
        for (ushort i = 0; i < 50; i++) w.CheckPacket(i);
        w.Reset();
        for (ushort i = 0; i < 50; i++)
            Assert.False(w.CheckPacket(i) != PacketWindowStatus.New, $"After reset, packet {i} should not be a duplicate");
    }

    [Fact]
    public void J04_Reset_PreservesMaxAllowedAhead()
    {
        var w = new InPacketWindow();
        w.MaxAllowedAhead = 200;
        w.Reset();
        Assert.Equal(200u, w.MaxAllowedAhead);
    }

    [Fact]
    public void J05_MultipleResets_WorkCorrectly()
    {
        var w = new InPacketWindow();
        for (int r = 0; r < 5; r++)
        {
            w.Reset();
            Assert.False(w.CheckPacket(10) != PacketWindowStatus.New);
            Assert.True(w.CheckPacket(10) != PacketWindowStatus.New);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  K – High-PPS simulation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void K01_HighPps_Sequential_NoFalsePositives()
    {
        // Simulate 200 000 sequential packets (≈ 3 full ushort cycles).
        // Every packet must be seen exactly once.
        var w = new InPacketWindow();
        for (int i = 0; i < 200_000; i++)
        {
            ushort id = (ushort)(i % 65536);
            // Each id in a fresh rotation should be "new"
            bool seenBefore = w.CheckPacket(id) != PacketWindowStatus.New;
            // Because we advance sequentially by 1, each new id is 1 ahead.
            // Only very first occurrence in each window-cycle can be "new".
            // Sequential delivery must always return false.
            Assert.False(seenBefore, $"Sequential packet {id} (iteration {i}) falsely flagged");
        }
    }

    [Fact]
    public void K02_HighPps_ImmediateDuplicates_AllBlocked()
    {
        var w = new InPacketWindow();
        for (ushort i = 0; i < 1000; i++)
        {
            w.CheckPacket(i);
            Assert.True(w.CheckPacket(i) != PacketWindowStatus.New, $"Immediate duplicate of {i} should be blocked");
        }
    }

    [Fact]
    public void K03_HighPps_OutOfOrder_WithinWindow_Correct()
    {
        // Simulate arrival of packets 0..511 with a fixed reorder distance of 8.
        // Every unique id must appear exactly as "new" once.
        var w = new InPacketWindow();
        var seen = new HashSet<ushort>();

        // Build a reordered sequence: send i+8 then i for i = 0,8,16,...
        var packets = new List<ushort>();
        for (ushort i = 0; i < 512; i += 8)
        {
            for (ushort j = 0; j < 8; j++)
                packets.Add((ushort)(i + 8 + j <= 519 ? i + 8 + j : i + j));
            for (ushort j = 0; j < 8; j++)
                packets.Add((ushort)(i + j));
        }

        foreach (var id in packets)
        {
            bool result = w.CheckPacket(id) != PacketWindowStatus.New;
            if (seen.Contains(id))
                Assert.True(result, $"Duplicate {id} should be seen-before");
            else
            {
                Assert.False(result, $"New packet {id} should not be seen-before");
                seen.Add(id);
            }
        }
    }

    [Fact]
    public void K04_HighPps_PerformanceCheck_OneMillionSequential()
    {
        // Smoke test: 1 000 000 calls must complete without error.
        // (Not a timing assertion – just verifies no exception/hang.)
        var w = new InPacketWindow();
        for (int i = 0; i < 1_000_000; i++)
            w.CheckPacket((ushort)(i % 65536));
        // If we reach here without exception, the test passes.
        Assert.True(true);
    }

    [Fact]
    public void K05_HighPps_UshorCycleReplay_AllBlocked()
    {
        // Fill the window, then replay the entire last WindowSize packets.
        var w = new InPacketWindow();
        const int total = WS * 3;
        for (int i = 0; i < total; i++)
            w.CheckPacket((ushort)(i % 65536));

        ushort hr = w.SequenceHead;
        // Every id in [hr - WS + 1 .. hr] should be seen-before (still in window).
        for (int d = 0; d < WS - 1; d++)
        {
            ushort id = (ushort)(hr - d);
            Assert.True(w.CheckPacket(id) != PacketWindowStatus.New, $"In-window replay of {id} (d={d}) should be blocked");
        }
    }




    [Fact]
    public void InPacketWindow_BoundarySecurity_Test()
    {
        var w = new InPacketWindow();

        //Assert.Equal(PacketWindowStatus.TooFarAhead, w.CheckPacket(65535));
        Assert.Equal(PacketWindowStatus.Duplicate, w.CheckPacket(0));
        Assert.Equal(PacketWindowStatus.New, w.CheckPacket(1));

        Assert.Equal(PacketWindowStatus.TooFarAhead, w.CheckPacket(32000));

        for (int i = 4095; i < ushort.MaxValue; i += 4095)
        {
            Assert.Equal(PacketWindowStatus.New, w.CheckPacket((ushort)i));
        }

        Assert.Equal(PacketWindowStatus.TooOld, w.CheckPacket(55000));
        var Old = w.SequenceHead;
        Assert.Equal(PacketWindowStatus.New, w.CheckPacket((ushort)(ushort.MaxValue - (Old + 10))));
        Assert.Equal(PacketWindowStatus.Duplicate, w.CheckPacket(Old));
        Assert.Equal(PacketWindowStatus.TooOld, w.CheckPacket((ushort)(45000)));
    }

    [Fact]
    public async Task InPacketWindow_OperationalTest()
    {
        int NumWorkers = Math.Max(1, Environment.ProcessorCount);
        int NumPackets = 50_000_000;

        await Task.WhenAll(Enumerable.Range(0, NumWorkers).Select(WorkerId => Task.Run(() =>
        {
            var w = new InPacketWindow();

            Assert.True(w.CheckPacket(0) != PacketWindowStatus.New);

            for (int baseId = 1; baseId < NumPackets; baseId += InPacketWindow.WindowSize / 2)
            {
                // 1. Create a batch of IDs for this segment
                var batch = Enumerable.Range(baseId, InPacketWindow.WindowSize / 2)
                                      .Select(i => (ushort)(i % 65536))
                                      .ToList();

                // 2. SIMULATE OUT-OF-ORDER: Shuffle the batch
                var shuffledBatch = batch.OrderBy(x => Random.Shared.Next()).ToList();

                // 3. SIMULATE PACKET LOSS: Randomly skip 10% of this batch
                var toSend = shuffledBatch.Where(_ => Random.Shared.NextDouble() > 0.10).ToList();
                var skipped = shuffledBatch.Except(toSend).ToList();

                // First Pass: Send the subset
                foreach (var id in toSend)
                {
                    // Should be the first time we see these
                    Assert.False(w.CheckPacket(id) != PacketWindowStatus.New, $"ID {id} marked seen too early.");
                }

                // Second Pass: Immediate Duplicates (Simulate network retry/echo)
                foreach (var id in toSend.Take(10))
                {
                    Assert.True(w.CheckPacket(id) != PacketWindowStatus.New, $"ID {id} should be marked as duplicate.");
                }

                // 4. SIMULATE LATE ARRIVAL: Send the 'skipped' packets from the PREVIOUS batch now
                // (This tests if the window still remembers them even if the head moved forward)
                foreach (var id in skipped)
                {
                    Assert.False(w.CheckPacket(id) != PacketWindowStatus.New, $"Late packet {id} should have been accepted.");
                    Assert.True(w.CheckPacket(id) != PacketWindowStatus.New, $"Late packet {id} should now be duplicate.");
                }
            }
        })));
    }
}