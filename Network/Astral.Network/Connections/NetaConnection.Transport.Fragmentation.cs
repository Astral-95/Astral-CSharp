using Astral.Network.Interfaces;
using Astral.Network.Enums;
using Astral.Network.Transport;
using Astral.Serialization;
using Astral.Containers;

namespace Astral.Network.Connections;

public partial class NetaConnection : INetworkObject
{
    class FragmentedPacket
    {
        protected int InPool = 0;
        private static readonly ConcurrentFastQueue<FragmentedPacket> Pool = new();

        public Neta_FragmentGroupIndexType GroupIndex;
        public Neta_FragmentIndexType Index;
        public Neta_FragmentNumFragsType NumFrags;

        public PooledInPacket Packet = null!;

        FragmentedPacket() { }
        void Set(PooledInPacket Packet)
        {
            this.Packet = Packet;

            Packet.Serialize(ref Index);
            Packet.Serialize(ref GroupIndex);
            Packet.Serialize(ref NumFrags);
        }

        static internal FragmentedPacket Rent(PooledInPacket Packet)
        {
            if (Pool.TryDequeue(out var Frag))
            {
                Frag.InPool = 0;
            }
            else
            {
                Frag = new FragmentedPacket();
                PooledObjectsTracker.OnNewPoolObject();
            }
            Frag.Set(Packet);
#if NETA_DEBUG
            PooledObjectsTracker.Register(Frag);
#endif
            return Frag;
        }

        public void Return()
        {
#if NETA_DEBUG
            var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
            if (Val != 0) throw new InvalidOperationException("Attempted to return a fragment that is already in the pool.");
            PooledObjectsTracker.Unregister(this);
#endif
            Pool.Enqueue(this);
        }
    }

    class FragmentedPacketsGroup
    {
        public Neta_FragmentGroupIndexType Id = 0;
        public Neta_FragmentIndexType NumFragsAdded = 0;
        public List<FragmentedPacket?> Fragments = new List<FragmentedPacket?>(4);
    }

    public struct PacketFragmentationHandler
    {
        NetaConnection Connection;

        Neta_FragmentGroupIndexType NextGroupIndex = 0;
        const Neta_FragmentGroupIndexType MaxGroupIndex = 2048;

        FragmentedPacketsGroup[] Groups = new FragmentedPacketsGroup[MaxGroupIndex];

        internal PacketFragmentationHandler(NetaConnection Connection)
        {
            this.Connection = Connection;
            for (int Index = 0; Index < MaxGroupIndex; Index++)
            {
                Groups[Index] = new FragmentedPacketsGroup();
            }
        }

        class PacketFragmentationHandler_ProcessIncomingPacket { }
        class PacketFragmentationHandler_ProcessIncomingPacket_2 { }
        public PooledInPacket? Process(PooledInPacket Packet)
        {
            FragmentedPacket Frag = FragmentedPacket.Rent(Packet);

#if NETA_DEBUG
            if (Groups.Count() < Frag.GroupIndex)
            {
                Frag.Return();
                throw new InvalidOperationException($"FragCount: [{Groups.Count()}] - GroupIndex: [{Frag.GroupIndex}]");
            }
#endif
            FragmentedPacketsGroup Group = Groups[Frag.GroupIndex];

            if (Group.Fragments.Count == 0)
            {
#if NETA_DEBUG
                if (Frag.NumFrags < 1)
                {
                    Frag.Return();
                    throw new InvalidOperationException($"Frag.NumFrags < 1.\nFragIndex: [{Frag.Index}] - NumFrags: [{Frag.NumFrags}] - GroupIndex: [{Frag.GroupIndex}] - PktFlags: [{Packet.Flags}] PktMsg: - [{Packet.Message}]");
                }
#endif
                for (int i = 0; i < Frag.NumFrags; i++)
                {
                    Group.Fragments.Add(null);
                }
            }

            Group.NumFragsAdded++;
#if NETA_DEBUG
            if (Frag.Index >= Group.Fragments.Count)
            {
                Frag.Return();
                throw new InvalidOperationException($"Frag.Index >= Group.Fragments.Count.\nIndex: [{Frag.Index}] Count: [{Group.Fragments.Count}]");
            }
            if (Group.Fragments[Frag.Index] != null)
            {
                Frag.Return();
                throw new InvalidOperationException($"Duplicate frag id, likely due to Group overflow.\nFragCount: [{Group.Fragments.Count()}] - FragIndex: {Frag.Index} - GroupIndex: {Frag.GroupIndex}");
            }
#endif
            Group.Fragments[Frag.Index] = Frag;


            if (Frag.NumFrags > Group.NumFragsAdded)
            {
                return null;
            }

            Packet = Group.Fragments[0]!.Packet;

#if NETA_DEBUG
            if (Packet == null) throw new InvalidOperationException();
#endif
            var Writer = PooledByteWriter.Rent<PacketFragmentationHandler_ProcessIncomingPacket>();
            foreach (var ExFrag in Group.Fragments)
            {
#if NETA_DEBUG
                if (ExFrag == null) throw new InvalidOperationException();
                if (ExFrag.Packet == null) throw new InvalidOperationException();
#endif
                Writer.Serialize(ExFrag.Packet);
            }

            var CombinePacket = PooledInPacket.Rent<PacketFragmentationHandler_ProcessIncomingPacket_2>(Writer);

            foreach (var ExFrag in Group.Fragments)
            {
                ExFrag!.Packet.Return();
                ExFrag.Return();
            }

            Group.NumFragsAdded = 0;
            Group.Fragments.Clear();

            Writer.Return();
            return CombinePacket;
        }


        Neta_FragmentGroupIndexType GetNextGroupIndex()
        {
            NextGroupIndex++;

            if (NextGroupIndex < MaxGroupIndex)
            {
                return NextGroupIndex;
            }
            else
            {
                NextGroupIndex = 0;
                return 0;
            }
        }



        struct OutFragmentInfo
        {
            public PooledOutPacket Packet;
            public int NumBytes;
        }
        List<OutFragmentInfo> FragmentInfos = new List<OutFragmentInfo>(4);

        class PacketFragmentationHandler_ProcessOutgoingPacket_1 { }
        class PacketFragmentationHandler_ProcessOutgoingPacket_2 { }
        public void Process(PooledOutPacket Packet, List<PooledOutPacket> OutPackets, Int64 TicksNow)
        {
#if NETA_DEBUG
            if ((Packet.Flags & EPacketFlags.HasAcksOnly) != 0)
            {
                throw new InvalidOperationException();
            }
#endif
#if NETA_DEBUG
            if (FragmentInfos.Count > 0)
            {
                throw new InvalidOperationException("FragmentInfos is not empty.");
            }

            if (Packet.Pos <= NetaConsts.BufferMaxSizeBytes)
            {
                throw new InvalidOperationException($"ProcessOutgoingPacket only accepts packets larger than [{nameof(NetaConsts.BufferMaxSizeBytes)}]\nReceived size: [{Packet.Pos}] - Min size expected: [{NetaConsts.BufferMaxSizeBytes + 1}]");
            }
#endif
            Packet.Flags |= Enums.EPacketFlags.Fragment;

            // Remove standard header [NetaConsts.ReliableHeaderSizeBytes] 
            int NumBytes = Packet.Pos - Packet.HeaderBytes;

            // Create first packet as raw, we will take headers from original packet
            var NewPkt = PooledOutPacket.RentReliable<PacketFragmentationHandler_ProcessOutgoingPacket_1>(Packet.Id, Packet.Message);
            NewPkt.Flags = Packet.Flags;

            // Has additional header/s?
            if (Packet.HeaderBytes > NetaConsts.ReliableHeaderSizeBytes)
            {
                // Add the additional header/s to the new packet
                NewPkt.Serialize(Packet.GetBuffer(), NetaConsts.ReliableHeaderSizeBytes, Packet.HeaderBytes - NetaConsts.ReliableHeaderSizeBytes);
            }

            var Bytes = NetaConsts.BufferMaxSizeBytes - NewPkt.Pos - NetaConsts.PartialHeaderSizeBytes;
            NumBytes -= Bytes;

            OutFragmentInfo FragmentInfo = new OutFragmentInfo();
            FragmentInfo.Packet = NewPkt;
            FragmentInfo.NumBytes = Bytes;
            FragmentInfos.Add(FragmentInfo);

            Packet.Flags &= ~(EPacketFlags.HasAcks | EPacketFlags.HasTimestamp);

            while (NumBytes > 0)
            {
                NewPkt = Connection.CreateReliablePacket<PacketFragmentationHandler_ProcessOutgoingPacket_2>(Packet.Message);
                NewPkt.Flags |= Packet.Flags;

                Bytes = Math.Min(NumBytes, NetaConsts.BufferMaxSizeBytes - NewPkt.Pos - NetaConsts.PartialHeaderSizeBytes);
                NumBytes -= Bytes;

                FragmentInfo = new OutFragmentInfo();
                FragmentInfo.Packet = NewPkt;
                FragmentInfo.NumBytes = Bytes;
                FragmentInfos.Add(FragmentInfo);
            }

#if NETA_DEBUG
            if (FragmentInfos.Count > 256)
            {
                throw new InvalidOperationException("Packet is TOO large, it will result in more than 256 fragments.");
            }
#endif
            int StartIndex = Packet.HeaderBytes;
            int RemainingBytes = Packet.Pos - Packet.HeaderBytes;
            byte NumFrags = (byte)FragmentInfos.Count;
            var Group = GetNextGroupIndex();

            for (byte Index = 0; Index < FragmentInfos.Count; Index++)
            {
                FragmentInfo = FragmentInfos[Index];

                FragmentInfo.Packet.Serialize(Index);
                FragmentInfo.Packet.Serialize(Group);
                FragmentInfo.Packet.Serialize(NumFrags);

                FragmentInfo.Packet.Serialize(Packet.GetBuffer(), StartIndex, FragmentInfo.NumBytes);
                FragmentInfo.Packet.FinalizePacketAsFragment(TicksNow);
                OutPackets.Add(FragmentInfo.Packet);

                StartIndex += FragmentInfo.NumBytes;
                RemainingBytes -= FragmentInfo.NumBytes;
            }
            FragmentInfos.Clear();
            Packet.Return();
        }
        public void Reset()
        {
            foreach (var Info in FragmentInfos) Info.Packet.Return();
            foreach (var Fragment in Groups)
            {
                foreach (var FragmentedPacket in Fragment.Fragments)
                {
                    if (FragmentedPacket == null) continue;
                    FragmentedPacket.Packet.Return();
                    FragmentedPacket.Return();
                }
            }
        }
    }


    PacketFragmentationHandler PktFragmentationHandler;

    void Init_FragmentationHandler()
    {
        PktFragmentationHandler = new PacketFragmentationHandler(this);
    }
}