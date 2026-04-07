namespace Astral.Containers;

public static class DefaultTags
{
    public static readonly Tag None = Tag.Create("None");
}
public readonly struct Tag : IEquatable<Tag>
{
    public readonly int Id;
    private readonly int ParentId;

    private struct TagInfo { public Name Name; public int ParentId; }
    private static readonly List<TagInfo> Tags = new();

    // Public static property controlling registration
    private static bool _registrationOpen = true;

    public static readonly Tag None = Create("None");

    private Tag(int id, int parentId) { Id = id; ParentId = parentId; }

    // Public factory — can be called anywhere, but will throw after static init
    public static Tag Create(string dotPath)
    {
        if (!_registrationOpen)
            throw new InvalidOperationException("Cannot create new tags at runtime.");

        var parts = dotPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int parentId = -1;
        Tag last = default;

        foreach (var part in parts)
        {
            var name = new Name(part);
            int existingId = Tags.FindIndex(t => t.Name == name && t.ParentId == parentId);
            if (existingId >= 0)
                last = new Tag(existingId, parentId);
            else
            {
                int id = Tags.Count;
                Tags.Add(new TagInfo { Name = name, ParentId = parentId });
                last = new Tag(id, parentId);
            }

            parentId = last.Id;
        }

        return last;
    }

    // Call this at the end of static initialization
    public static void FinishRegistration() => _registrationOpen = false;

    public Name GetName() => Tags[Id].Name;

    public bool IsChildOf(Tag parent)
    {
        int current = Id;
        while (current != -1)
        {
            if (current == parent.Id) return true;
            current = Tags[current].ParentId;
        }
        return false;
    }

    public bool Equals(Tag other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is Tag other && Equals(other);
    public override int GetHashCode() => Id;
    public static bool operator ==(Tag a, Tag b) => a.Id == b.Id;
    public static bool operator !=(Tag a, Tag b) => a.Id != b.Id;

    public static implicit operator string(Tag Tag) => Tag;
}