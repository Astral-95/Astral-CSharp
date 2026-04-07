using System.Collections.Concurrent;

namespace Astral.Containers;

public struct Name : IEquatable<Name>
{
    public readonly int Id;
    private static readonly ConcurrentDictionary<string, int> NameToId = new();
    private static readonly ConcurrentDictionary<int, string> IdToName = new();
    private static int NextId = 0;

    static Name()
    {
        NameToId.TryAdd("None", 0);
        IdToName.TryAdd(0, "None");
    }
    public Name(string Str)
    {
        Id = NameToId.GetOrAdd(Str, key =>
        {
            int newId = Interlocked.Increment(ref NextId);
            IdToName[newId] = key;
            return newId;
        });
    }

    public Name(Name Other) => Id = Other.Id;

    public string GetString() => IdToName[Id];
    public readonly bool Equals(Name other) => Id == other.Id;
    public override readonly bool Equals(object? obj) => obj is Name other && Equals(other);
    public override int GetHashCode() => Id;
    public override string ToString() => GetString();
    public static bool operator ==(Name a, Name b) => a.Id == b.Id;
    public static bool operator !=(Name a, Name b) => a.Id != b.Id;


    public static implicit operator Name(string str) => new Name(str);
    public static implicit operator string(Name name) => name.GetString();
}