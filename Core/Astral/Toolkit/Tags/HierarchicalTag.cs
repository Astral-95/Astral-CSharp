using Astral.Serialization;
using System.Collections.Concurrent;

namespace Astral.HierarchicalTags;

public static class HierarchicalTagStatics
{
    private static bool RegistrationOpen = true;

    public static readonly ConcurrentDictionary<ulong, HierarchicalTag> HashToTag = new();
    public static readonly ConcurrentDictionary<ulong, string> HashToString = new();
    public static readonly ConcurrentDictionary<ulong, HierarchicalTagInfo> HashToTagInfo = new();

    static HierarchicalTagStatics()
    {
        HashToTagInfo.TryAdd(0, HierarchicalTagInfo.EmptyInfo);
    }

    public static HierarchicalTag CreateTag(string Str)
    {
        if (!RegistrationOpen)
            throw new InvalidOperationException("Cannot create new tags now.");

        var Hash = FNVHash(Str);
        if (HashToTag.TryGetValue(Hash, out var Value)) return Value;

        // 1. Identify all parent strings first (e.g., "A.B.C" -> ["A", "A.B"])
        var ParentStrings = new List<string>();
        string CurrentPath = Str;
        int DotIndex;

        while ((DotIndex = CurrentPath.LastIndexOf('.')) != -1)
        {
            CurrentPath = CurrentPath[..DotIndex];
            ParentStrings.Add(CurrentPath);
        }

        // 2. Resolve all parent HierarchicalTags
        var Parents = new List<HierarchicalTag>();
        foreach (var pStr in ParentStrings)
        {
            Parents.Add(CreateTag(pStr));
        }

        // 3. Create the Info object with the finished list
        var Info = new HierarchicalTagInfo(Hash, Parents);

        // 4. Create the final Readonly Struct
        var newTag = new HierarchicalTag(Hash, Info);

        // 5. Register everything
        HashToTag.TryAdd(Hash, newTag);
        HashToString.TryAdd(Hash, Str);
        HashToTagInfo.TryAdd(Hash, Info);

        return newTag;
    }

    public static void FinishRegistration() => RegistrationOpen = false;

    public static ulong FNVHash(string Str)
    {
        ulong hash = 14695981039346656037UL; // FNV offset basis
        foreach (char c in Str)
        {
            hash ^= c;
            hash *= 1099511628211UL; // FNV prime
        }
        return hash;
    }
}

public class HierarchicalTagInfo
{
    internal static readonly HierarchicalTagInfo EmptyInfo = new HierarchicalTagInfo();
    public ulong Hash;
    public List<HierarchicalTag> Parents;

    private HierarchicalTagInfo()
    {
        Hash = 0;
        Parents = new List<HierarchicalTag>();
    }
    internal HierarchicalTagInfo(ulong InHash, List<HierarchicalTag> InParents)
    {
        Hash = InHash;
        Parents = InParents;
    }
}
public readonly struct HierarchicalTag : IEquatable<HierarchicalTag>
{
    internal readonly HierarchicalTagInfo Info;

    public readonly ulong Hash;
    public HierarchicalTag()
    {
        Hash = 0;
        Info = HierarchicalTagInfo.EmptyInfo;
    }

    internal HierarchicalTag(ulong InHash, HierarchicalTagInfo InInfo)
    {
        Hash = InHash;
        Info = InInfo;
    }


    public bool IsValid() { return Hash != 0; }
    public bool Equals(HierarchicalTag Other) => Hash == Other.Hash;
    public override bool Equals(object? obj) => obj is HierarchicalTag Other && Equals(Other);
    public static bool operator ==(HierarchicalTag a, HierarchicalTag b) => a.Hash == b.Hash;
    public static bool operator !=(HierarchicalTag a, HierarchicalTag b) => a.Hash != b.Hash;

    public static implicit operator string(HierarchicalTag Tag) => Tag;

    // Same as ==
    public bool MatchesTagExact(HierarchicalTag Other) => Hash == Other.Hash;

    /// <summary>
    /// Matches rules (this tag against the "other" tag): <br/>
    /// - [A.B].MatchesTag( [A.B.C] )       = false <br/>
    /// - [A.B.C].MatchesTag( [A] )         = true <br/>
    /// - [A.B.C].MatchesTag( [A.B] )       = true <br/>
    /// - [A.B.C].MatchesTag( [A.B.C] )     = true
    /// </summary>
    /// <param name="Other">The tag to check against</param>
    /// <returns>true if this tag matches the other tag according to hierarchy rules</returns>
    public bool MatchesTag(HierarchicalTag Other)
    {
        if (this == Other) return true;

        return Info.Parents.Contains(Other);
    }

    public override int GetHashCode()
    {
        return (int)(Hash ^ Hash >> 32); // fold high bits into low bits
    }


    public void NetSerialize(ByteWriter Writer) => Writer.Serialize(Hash);
    public static HierarchicalTag NetSerialize(ByteReader Reader)
    {
        var Hash = Reader.Serialize<ulong>();
        HierarchicalTagStatics.HashToTag.TryGetValue(Hash, out var Tag);
        return Tag;
    }
}