using Astral.HierarchicalTags;

namespace Astral.UnitTests.Toolkit.HierarchicalTags;

public class HierarchicalTagContainerTests
{

    [Fact]
    public void HasTag()
    {
        HierarchicalTagContainer TagContainer = new HierarchicalTagContainer();

        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest));
        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);

        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest));
        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        TagContainer.RemoveTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);

        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest));
        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TagContainer.HasTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));
    }

    [Fact]
    public void HasTagExact()
    {
        HierarchicalTagContainer TagContainer = new HierarchicalTagContainer();

        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest));
        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);

        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest));
        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.True(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        TagContainer.RemoveTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);

        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest));
        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TagContainer.HasTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));
    }


    [Fact]
    public void HasAny()
    {
        HierarchicalTagContainer TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        HierarchicalTagContainer OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.True(TagContainer.HasAny(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.True(TagContainer.HasAny(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item_Store_Default);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.True(TagContainer.HasAny(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item_Store_Default);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);

        Assert.False(TagContainer.HasAny(OtherTagContainer));
    }

    [Fact]
    public void HasAnyExact()
    {
        HierarchicalTagContainer TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        HierarchicalTagContainer OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.True(TagContainer.HasAnyExact(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.False(TagContainer.HasAnyExact(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item_Store_Default);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.False(TagContainer.HasAnyExact(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item_Store_Default);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);

        Assert.False(TagContainer.HasAnyExact(OtherTagContainer));
    }


    [Fact]
    public void HasAll()
    {
        HierarchicalTagContainer TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        HierarchicalTagContainer OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.True(TagContainer.HasAll(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        Assert.True(TagContainer.HasAll(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.False(TagContainer.HasAll(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item_Store_Default);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.False(TagContainer.HasAll(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item_Store_Default);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);

        Assert.False(TagContainer.HasAll(OtherTagContainer));
    }


    [Fact]
    public void HasAllExact()
    {
        HierarchicalTagContainer TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        HierarchicalTagContainer OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.True(TagContainer.HasAllExact(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        Assert.True(TagContainer.HasAllExact(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        Assert.False(TagContainer.HasAllExact(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.False(TagContainer.HasAllExact(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item_Store_Default);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot);

        Assert.False(TagContainer.HasAllExact(OtherTagContainer));


        TagContainer = new HierarchicalTagContainer();
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default);
        TagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Item_Store_Default);

        OtherTagContainer = new HierarchicalTagContainer();
        OtherTagContainer.AddTag(TesterHierarchicalTags.TagTest_Inventory_Container);

        Assert.False(TagContainer.HasAllExact(OtherTagContainer));
    }
}