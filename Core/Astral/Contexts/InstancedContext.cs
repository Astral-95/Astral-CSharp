using Astral.Interfaces;
using Astral.Diagnostics;

namespace Astral
{
	public class InstancedContext
    {
		private const int ObjectIdChunkSize = 4096;
		private const int ObjectIdGenerationBits = 4;
		private const int ObjectIdIndexBits = 32 - ObjectIdGenerationBits;
		private const uint ObjectIdIndexMask = (1u << ObjectIdIndexBits) - 1;          // 0x0FFFFFFF
		private const uint ObjectIdGenerationMask = (1u << ObjectIdGenerationBits) - 1; // 0xF
	
		private readonly List<IObject[]> ObjectIdChunks = new();
		private readonly List<uint> ObjectIdGenerations = new();
		private readonly Stack<uint> ObjectIdFreeList = new();
		private readonly Queue<(uint Index, DateTime FreeTime)> ObjectIdDelayedFree = new();
		private readonly TimeSpan ObjectIdReuseDelay = TimeSpan.FromMinutes(1);
	
		public InstancedContext() { }
	
		public void RegisterObject(IObject Obj)
		{
			if (Obj.ObjectId != 0) return;
	
			ProcessDelayedFrees();
	
			uint Index;
			uint Generation;
	
			if (ObjectIdFreeList.Count > 0)
			{
				Index = ObjectIdFreeList.Pop();
				Generation = (ObjectIdGenerations[(int)Index] + 1) & ObjectIdGenerationMask; // wrap on 4 bits
				ObjectIdGenerations[(int)Index] = Generation;
			}
			else
			{
				Index = (uint)ObjectIdGenerations.Count;
				Generation = 1;
				ObjectIdGenerations.Add(Generation);
			}
	
			int ChunkIndex = (int)(Index / ObjectIdChunkSize);
			int LocalIndex = (int)(Index % ObjectIdChunkSize);
	
			while (ChunkIndex >= ObjectIdChunks.Count)
				ObjectIdChunks.Add(new IObject[ObjectIdChunkSize]);
	
			ObjectIdChunks[ChunkIndex][LocalIndex] = Obj;
	
            Obj.SetObjectId((Generation << ObjectIdIndexBits) | Index);
		}
	
		public void RegisterObject(IObject Obj, uint ObjectId)
		{
			if (Obj == null || Obj.ObjectId != 0) return;
	
			uint Index = ObjectId & ObjectIdIndexMask;
			uint Generation = (ObjectId >> ObjectIdIndexBits) & ObjectIdGenerationMask;
	
			// Ensure Generations list can hold this index
			while (Index >= ObjectIdGenerations.Count)
				ObjectIdGenerations.Add(0);
	
			// Check for conflicts
			Guard.Assert(ObjectIdGenerations[(int)Index] == 0 || ObjectIdGenerations[(int)Index] == Generation, $"ObjectId //{ObjectId} is already in use.");
	
			ObjectIdGenerations[(int)Index] = Generation;
	
			int ChunkIndex = (int)(Index / ObjectIdChunkSize);
			int LocalIndex = (int)(Index % ObjectIdChunkSize);
	
			while (ChunkIndex >= ObjectIdChunks.Count)
				ObjectIdChunks.Add(new IObject[ObjectIdChunkSize]);
	
			ObjectIdChunks[ChunkIndex][LocalIndex] = Obj;
	
            Obj.SetObjectId(ObjectId);
		}
	
	
		public void UnregisterObject(IObject Obj)
		{
			if (Obj == null) return;
			var TargetId = Obj.ObjectId;
            Obj.SetObjectId(0);
	
            uint Index = TargetId & ObjectIdIndexMask;
			uint Generation = (TargetId >> ObjectIdIndexBits) & ObjectIdGenerationMask;
	
			if (Index >= ObjectIdGenerations.Count) return;
			if (ObjectIdGenerations[(int)Index] != Generation) return;
	
			int ChunkIndex = (int)(Index / ObjectIdChunkSize);
			int LocalIndex = (int)(Index % ObjectIdChunkSize);
	
			ObjectIdChunks[ChunkIndex][LocalIndex] = default!;
	
			ObjectIdDelayedFree.Enqueue((Index, DateTime.UtcNow + ObjectIdReuseDelay));
		}
	
		public IObject? GetObject(uint ObjectId)
		{
			uint index = ObjectId & ObjectIdIndexMask;
			uint generation = (ObjectId >> ObjectIdIndexBits) & ObjectIdGenerationMask;
	
			if (index >= ObjectIdGenerations.Count) return null;
			if (ObjectIdGenerations[(int)index] != generation) return null;
	
			int chunkIndex = (int)(index / ObjectIdChunkSize);
			int localIndex = (int)(index % ObjectIdChunkSize);
	
			if (chunkIndex >= ObjectIdChunks.Count) return null;
	
			return ObjectIdChunks[chunkIndex][localIndex];
		}
	
		// Move expired delayed frees into free-list
		private void ProcessDelayedFrees()
		{
			while (ObjectIdDelayedFree.Count > 0 && ObjectIdDelayedFree.Peek().FreeTime <= DateTime.UtcNow)
			{
				ObjectIdFreeList.Push(ObjectIdDelayedFree.Dequeue().Index);
			}
		}
	}
}
