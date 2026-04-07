namespace Astral.UnitTests.Toolkit.HierarchicalTags;

public class HierarchicalTagTests
{
    [Fact]
    public void MatchesTag()
    {
        Assert.True(TesterHierarchicalTags.TagTest.MatchesTag(TesterHierarchicalTags.TagTest));
        Assert.False(TesterHierarchicalTags.TagTest.MatchesTag(TesterHierarchicalTags.TagTest_Inventory));
        Assert.False(TesterHierarchicalTags.TagTest.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TesterHierarchicalTags.TagTest.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TesterHierarchicalTags.TagTest.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        Assert.True(TesterHierarchicalTags.TagTest_Inventory.MatchesTag(TesterHierarchicalTags.TagTest));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory.MatchesTag(TesterHierarchicalTags.TagTest_Inventory));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTag(TesterHierarchicalTags.TagTest));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTag(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTag(TesterHierarchicalTags.TagTest));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTag(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTag(TesterHierarchicalTags.TagTest));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTag(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTag(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));
    }

    [Fact]

    public void MatchesTagExact()
    {
        Assert.True(TesterHierarchicalTags.TagTest.MatchesTagExact(TesterHierarchicalTags.TagTest));
        Assert.False(TesterHierarchicalTags.TagTest.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory));
        Assert.False(TesterHierarchicalTags.TagTest.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TesterHierarchicalTags.TagTest.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TesterHierarchicalTags.TagTest.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        Assert.False(TesterHierarchicalTags.TagTest_Inventory.MatchesTagExact(TesterHierarchicalTags.TagTest));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTagExact(TesterHierarchicalTags.TagTest));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTagExact(TesterHierarchicalTags.TagTest));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot_Store.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));

        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTagExact(TesterHierarchicalTags.TagTest));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot));
        Assert.False(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store));
        Assert.True(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default.MatchesTagExact(TesterHierarchicalTags.TagTest_Inventory_Slot_Store_Default));
    }
}