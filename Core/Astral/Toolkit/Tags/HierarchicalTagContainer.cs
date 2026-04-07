using Astral.Serialization;

namespace Astral.HierarchicalTags;

public class HierarchicalTagContainer
{
    private Dictionary<HierarchicalTag, UInt16> Tags = new Dictionary<HierarchicalTag, UInt16>();
    private Dictionary<HierarchicalTag, UInt16> ParentTags = new Dictionary<HierarchicalTag, UInt16>();

    public void AddTag(HierarchicalTag Tag)
    {
        if (!Tag.IsValid()) return;

        if (!Tags.TryAdd(Tag, 1))
        {
            Tags[Tag]++;
            return;
        }

        foreach (var Parent in Tag.Info.Parents)
        {
            if (!ParentTags.TryAdd(Parent, 1))
                ParentTags[Parent]++;
        }
    }
    public void AddTagUnique(HierarchicalTag Tag)
    {
        if (!Tag.IsValid() || Tags.TryAdd(Tag, 1)) return;

        foreach (var Parent in Tag.Info.Parents)
        {
            if (!ParentTags.TryAdd(Parent, 1))
                ParentTags[Parent]++;
        }
    }

    public bool RemoveTag(HierarchicalTag Tag)
    {
        if (!Tag.IsValid() || !Tags.TryGetValue(Tag, out var TagCount)) return false;

        if (TagCount > 1)
        {
            Tags[Tag]--;
            return true;
        }

        Tags.Remove(Tag);
        foreach (var Parent in Tag.Info.Parents)
        {
            if (!ParentTags.TryGetValue(Parent, out var ParentTagCount))
            {
                throw new Exception("HierarchicalTagContainer.RemoveTag: Unexcepted path.");
            }

            if (ParentTagCount <= 1) ParentTags.Remove(Parent);
            else ParentTags[Parent]--;
        }
        return true;
    }

    public bool HasTag(HierarchicalTag Tag) => Tags.ContainsKey(Tag) || ParentTags.ContainsKey(Tag);
    public bool HasTagExact(HierarchicalTag Tag) => Tags.ContainsKey(Tag);

    /// <summary>
    /// Checks if this container contains ANY of the tags in the specified container. <br/>
    /// - [A.1].HasAny( [A] ) = true <br/>
    /// - [A.1].HasAny( [A.1] ) = true <br/>
    /// - [A.1].HasAny( [A.1.1] ) = false <br/>
    /// </summary>
    /// <param name="ContainerToCheck"></param>
    /// <returns></returns>
    public bool HasAny(HierarchicalTagContainer ContainerToCheck)
    {
        if (ContainerToCheck == null || ContainerToCheck.IsEmpty()) return false;

        foreach (var OtherTag in ContainerToCheck.GetTags())
        {
            if (Tags.ContainsKey(OtherTag) || ParentTags.ContainsKey(OtherTag)) return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if this container contains ANY of the tags in the specified container, only allowing exact matches. <br/>
    /// - [A.1].HasAnyExact( [A] ) = false <br/>
    /// - [A.1].HasAnyExact( [A.1] ) = true <br/>
    /// - [A.1].HasAnyExact( [A.1.1] ) = false <br/>
    /// </summary>
    /// <param name="ContainerToCheck"></param>
    /// <returns></returns>
    public bool HasAnyExact(HierarchicalTagContainer ContainerToCheck)
    {
        if (ContainerToCheck == null || ContainerToCheck.IsEmpty()) return false;

        foreach (var OtherTag in ContainerToCheck.GetTags())
        {
            if (Tags.ContainsKey(OtherTag)) return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if this container contains ALL of the tags in the specified container. <br/>
    /// - [A.1, B.1].HasAll( [A] ) = true <br/>
    /// - [A.1, B.1].HasAll( [A.1] ) = true <br/>
    /// - [A.1, B.1].HasAll( [A.1, B.1] ) = true <br/>
    /// - [A.1, B].HasAll( [A.1, B.1] ) = false <br/>
    /// - [A.1, B.1].HasAll( [A.1, C.1] ) = false <br/>
    /// Will return false if a tag in [ContainerToCheck] does not exist in this container. <br/>
    /// Will also return false if [ContainerToCheck] is null or empty.
    /// </summary>
    /// <param name="ContainerToCheck"></param>
    /// <returns></returns>
    public bool HasAll(HierarchicalTagContainer ContainerToCheck)
    {
        if (ContainerToCheck == null || ContainerToCheck.IsEmpty()) return false;

        foreach (var OtherTag in ContainerToCheck.GetTags())
        {
            if (!Tags.ContainsKey(OtherTag) && !ParentTags.ContainsKey(OtherTag)) return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if this container contains ALL of the tags in the specified container, only allowing exact matches. <br/>
    /// - [A.1, B.1].HasAll( [A] ) = false <br/>
    /// - [A.1, B.1].HasAll( [A.1] ) = false <br/>
    /// - [A.1, B.1].HasAll( [A.1, B.1] ) = true <br/>
    /// - [A.1, B.1, C.1].HasAll( [A.1, B.1] ) = true <br/>
    /// - [A.1, B.1, C.1].HasAll( [A.1, D.1] ) = false <br/>
    /// Will return false if a tag in [ContainerToCheck] does not exist in this container. <br/>
    /// Will also return false if [ContainerToCheck] is null or empty.
    /// </summary>
    /// <param name="ContainerToCheck"></param>
    /// <returns></returns>
    public bool HasAllExact(HierarchicalTagContainer ContainerToCheck)
    {
        if (ContainerToCheck == null || ContainerToCheck.IsEmpty()) return false;

        foreach (var OtherTag in ContainerToCheck.GetTags())
        {
            if (!Tags.ContainsKey(OtherTag)) return false;
        }

        return true;
    }

    public bool IsEmpty() => Tags.Count == 0;
    public IEnumerable<HierarchicalTag> GetTags() => Tags.Keys;
    public IEnumerable<HierarchicalTag> GetParentTags() => ParentTags.Keys;

    /// <summary>
    /// Returns wehther the container has any unique tags
    /// </summary>
    /// <returns></returns>
    public bool IsValid() => Tags.Count > 0;

    /// <summary>
    /// Returns the number of explicitly added tags, Unique tags.
    /// </summary>
    public int Count => Tags.Count;


    public void NetSerialize(ByteWriter Writer)
    {
        Writer.Serialize(Tags.Count);
        if (IsEmpty()) return;

        foreach (var Pair in Tags)
        {
            Pair.Key.NetSerialize(Writer);
            Writer.Serialize(Pair.Value);
        }
    }
    public void NetSerialize(ByteReader Reader)
    {
        Tags.Clear();
        ParentTags.Clear();
        var Count = Reader.Serialize<UInt16>();
        if (Count < 1) return;

        for (int i = 0; i < Count; i++)
        {
            var Tag = HierarchicalTag.NetSerialize(Reader);
            var TagCount = Reader.Serialize<UInt16>();

            for (UInt16 j = 0; j < TagCount; j++)
            {
                AddTag(Tag);
            }
        }
    }
}