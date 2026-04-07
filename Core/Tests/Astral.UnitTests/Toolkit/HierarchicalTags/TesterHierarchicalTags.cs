using Astral.HierarchicalTags;

namespace Astral.UnitTests.Toolkit.HierarchicalTags;

public static class TesterHierarchicalTags
{
    public static readonly HierarchicalTag TagTest = HierarchicalTagStatics.CreateTag("TagTest");
    public static readonly HierarchicalTag TagTest_Inventory = HierarchicalTagStatics.CreateTag("TagTest.Inventory");

    public static readonly HierarchicalTag TagTest_Inventory_Container = HierarchicalTagStatics.CreateTag("TagTest.Inventory.Container");
    public static readonly HierarchicalTag TagTest_Inventory_Container_Store = HierarchicalTagStatics.CreateTag("TagTest.Inventory.Container.Store");
    public static readonly HierarchicalTag TagTest_Inventory_Container_Store_Default = HierarchicalTagStatics.CreateTag("TagTest.Inventory.Container.Store.Default");

    public static readonly HierarchicalTag TagTest_Inventory_Slot = HierarchicalTagStatics.CreateTag("TagTest.Inventory.Slot");
    public static readonly HierarchicalTag TagTest_Inventory_Slot_Store = HierarchicalTagStatics.CreateTag("TagTest.Inventory.Slot.Store");
    public static readonly HierarchicalTag TagTest_Inventory_Slot_Store_Default = HierarchicalTagStatics.CreateTag("TagTest.Inventory.Slot.Store.Default");

    public static readonly HierarchicalTag TagTest_Inventory_Item = HierarchicalTagStatics.CreateTag("TagTest.Inventory.Item");
    public static readonly HierarchicalTag TagTest_Inventory_Item_Store = HierarchicalTagStatics.CreateTag("TagTest.Inventory.Item.Store");
    public static readonly HierarchicalTag TagTest_Inventory_Item_Store_Default = HierarchicalTagStatics.CreateTag("TagTest.Inventory.Item.Store.Default");
}