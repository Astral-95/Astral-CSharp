using Astral.Interfaces;
using Astral.Serialization;
using Astral.Containers;
using Astral.Network.Interfaces;
using Astral.Network.Connections;
using Astral.Network.Channels;

namespace Astral.Network.PackageMaps;

public struct OutMapping
{
    public UInt32 NetId;
    public UInt32 OuterNetId;
    public Int32 ObjectIndex;
}
public class ConnectionPackageMap
{
    NetaConnection Connection;

    private const int ObjectIdChunkSize = 4096;
    private const int ObjectIdGenerationBits = 4;
    private const int ObjectIdIndexBits = 32 - ObjectIdGenerationBits;
    private const uint ObjectIdIndexMask = (1u << ObjectIdIndexBits) - 1;          // 0x0FFFFFFF
    private const uint ObjectIdGenerationMask = (1u << ObjectIdGenerationBits) - 1; // 0xF

    private readonly List<IObject?[]> ObjectIdChunks = new();
    private readonly List<uint> ObjectIdGenerations = new();
    private readonly Stack<uint> ObjectIdFreeList = new();
    private readonly Queue<(uint Index, DateTime FreeTime)> ObjectIdDelayedFree = new();
    private readonly TimeSpan ObjectIdReuseDelay = TimeSpan.FromMinutes(1);

    UInt32 NextDefaultObjectNetworkId = 256;

    public Dictionary<UInt32, IObject> IdObjectMappings { get; set; } = new();
    public Dictionary<IObject, UInt32> ObjectIdMappings = new();

    Dictionary<UInt32, byte> PendingAckObjIdsIn = new();
    List<UInt32> PendingAckObjIdsOut = new();


    internal protected List<OutMapping> OutDeltaMappings { get; set; } = new();

    public ConnectionPackageMap(NetaConnection Conn)
    {
        Connection = Conn;

        IdObjectMappings.Add(1, Conn);
        ObjectIdMappings.Add(Conn, 1);
    }

    public void MapChannel(NetaChannel Channel, UInt32 Id)
    {
        IdObjectMappings.Add(Id, Channel);
        ObjectIdMappings.Add(Channel, Id);
    }


    public UInt32 MapObject(IObject Obj)
    {
        var ObjOuter = Obj.Outer;
        if (ObjOuter == null) return 0;
        if (Obj.ObjectIndex < 0) return 0;

        UInt32 OuterNetworkId = 0;
        if (!ObjectIdMappings.TryGetValue(ObjOuter, out OuterNetworkId)) OuterNetworkId = MapObject(ObjOuter);
        if (OuterNetworkId == 0) return 0;

        var NetworkId = NextDefaultObjectNetworkId++;
        PendingAckObjIdsOut.Add(NetworkId);
        if (!PendingAckObjIdsIn.TryAdd(NetworkId, 1)) PendingAckObjIdsIn[NetworkId]++;
        ObjectIdMappings.Add(Obj, NetworkId);

        var NewMapping = new OutMapping();
        //NewMapping.Type = ENetworkObjectMappingType.DefaultObject;
        NewMapping.OuterNetId = OuterNetworkId;
        NewMapping.ObjectIndex = Obj.ObjectIndex;
        NewMapping.NetId = NetworkId;
        OutDeltaMappings.Add(NewMapping);
        return NetworkId;
    }

    public void UnmapObject(IObject Obj)
    {
        if (!ObjectIdMappings.Remove(Obj, out var NetworkId)) return;
        IdObjectMappings.Remove(NetworkId);
        PendingAckObjIdsIn.Remove(NetworkId);
        PendingAckObjIdsOut.Remove(NetworkId);

        for (int i = 0; i < OutDeltaMappings.Count; i++)
        {
            var OutDeltaMapping = OutDeltaMappings[i];

            if (OutDeltaMapping.NetId == NetworkId)
            {
                OutDeltaMappings.RemoveAt(i); break;
            }
        }
    }

    public void SerializeObject(INetworkObject? Obj, ByteWriter Writer)
    {
        if (Obj == null)
        {
            Writer.Serialize<UInt32>(0);
            return;
        }

        if (ObjectIdMappings.TryGetValue(Obj, out var ObjId))
        {
            Writer.Serialize<UInt32>(ObjId);
            return;
        }

        Writer.Serialize(MapObject(Obj));
    }


    public IObject? SerializeObject(ByteReader Reader)
    {
        return IdObjectMappings.GetValueOrDefault(Reader.Serialize<UInt32>());
        //UInt32 NetworkId = Reader.Serialize<UInt32>();
        //IdObjectMappings.TryGetValue(NetworkId, out var Object);
        //return Object;
    }


    public bool HasObjectMappingExports() { return OutDeltaMappings.Count > 0; }
    public bool HasObjectCreationExports() { return false; }
    public bool HasObjectAckExports() { return PendingAckObjIdsOut.Count > 0; }

    public void ExportObjectCreation(ByteWriter Writer)
    {
        Writer.Serialize(0);
    }

    public void ImportObjectCreations(ByteReader Reader)
    {
        Reader.Serialize<UInt16>();
    }

    public void ExportMappings(ByteWriter Writer)
    {
        Writer.Serialize((UInt16)OutDeltaMappings.Count);

        foreach (var Mapping in OutDeltaMappings)
        {
            Writer.Serialize(Mapping.OuterNetId);
            Writer.Serialize(Mapping.ObjectIndex);
            Writer.Serialize(Mapping.NetId);
        }

        OutDeltaMappings.Clear();
    }

    public void ImportMappings(ByteReader Reader)
    {
        ushort Count = Reader.Serialize<UInt16>();
        for (int i = 0; i < Count; ++i)
        {
            var OuterNetId = Reader.Serialize<UInt16>();
            var ObjIndex = Reader.Serialize<Int32>();
            var ObjNetId = Reader.Serialize<UInt16>();

            var ObjOuter = IdObjectMappings.GetValueOrDefault(OuterNetId);
            IObject? Obj = null;

            foreach (var SubObj in ObjOuter!.DefaultSubobjects)
            {
                if (SubObj.ObjectIndex == ObjIndex)
                {
                    Obj = SubObj; break;
                }
            }

            if (Obj == null) throw new Exception("Object was not found in outer subobjects list.");

            ObjectIdMappings.Add(Obj, ObjNetId);
            IdObjectMappings.Add(ObjNetId, Obj);
            PendingAckObjIdsOut.Add(ObjNetId);
        }
    }


    public void ExportObjectAcks(ByteWriter Writer)
    {
        Writer.Serialize(PendingAckObjIdsOut);
    }
    public void ImportObjectAcks(ByteReader Reader)
    {
        var MappingAcks = PooledList<UInt16>.Rent();
        Reader.Serialize(MappingAcks);

        foreach (var MappingAck in MappingAcks)
        {
            var Num = PendingAckObjIdsIn.GetValueOrDefault(MappingAck);

            if (Num > 1)
            {
                PendingAckObjIdsIn[MappingAck]--;
            }
            else
            {
                PendingAckObjIdsIn.Remove(MappingAck);
            }
        }
        MappingAcks.Return();
    }

    // === PRIVATE HELPERS ===

    private void MapLocal(uint LocalId, uint RemoteIndex, byte RemoteGen, uint ClientGlobalId, bool trackForExport)
    {
        //EnsureLocalCapacity(LocalId);
        //
        //LocalIdToClientGlobal[(int)LocalId - 1] = ClientGlobalId;
        //LocalIdToRemoteIndex[(int)LocalId - 1] = RemoteIndex;
        //
        //int Chunk = (int)(RemoteIndex / RemoteIndexChunkSize);
        //int Local = (int)(RemoteIndex % RemoteIndexChunkSize);
        //EnsureRemoteChunk(Chunk);
        //
        //RemoteIndexChunks[Chunk][Local] = LocalId;
        //RemoteGenerationChunks[Chunk][Local] = RemoteGen;
        //
        //if (trackForExport) NewlyMappedLocalIds.Add(LocalId);
    }

    private void EnsureLocalCapacity(uint LocalId)
    {
        int Needed = (int)LocalId;
        //while (LocalIdToClientGlobal.Count < Needed)
        //{
        //	LocalIdToClientGlobal.Add(0);
        //	LocalIdToRemoteIndex.Add(0);
        //}
    }

    private void EnsureRemoteChunk(int Chunk)
    {
        //while (Chunk >= RemoteIndexChunks.Count)
        //{
        //	RemoteIndexChunks.Add(new uint[RemoteIndexChunkSize]);
        //	RemoteGenerationChunks.Add(new byte[RemoteIndexChunkSize]);
        //}
    }

    private uint AllocateLocalId()
    {
        //if (FreeLocalIds.Count > 0)
        //	return FreeLocalIds.Pop();
        //
        //return (uint)(LocalIdToClientGlobal.Count + 1);
        return 0;
    }

    //private bool TryGetLocalIdFromGlobal(uint GlobalId, out uint LocalId)
    //{
    //	// O(n) scan, or implement reverse mapping if needed
    //	for (int i = 0; i < LocalIdToClientGlobal.Count; i++)
    //	{
    //		if (LocalIdToClientGlobal[i] == GlobalId)
    //		{
    //			LocalId = (uint)(i + 1);
    //			return true;
    //		}
    //	}
    //	
    //	LocalId = 0;
    //	return false;
    //}

    //private bool TryGetGlobalIdByRemoteLocalId(uint LocalId, out uint ClientGlobalId)
    //{
    //	if (LocalId == 0 || LocalId > LocalIdToClientGlobal.Count)
    //	{
    //		ClientGlobalId = 0;
    //		return false;
    //	}
    //
    //	ClientGlobalId = LocalIdToClientGlobal[(int)LocalId - 1];
    //	return ClientGlobalId != 0;
    //}
}