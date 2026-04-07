using Astral.Attributes;
using Astral.Interfaces;
using Astral.Network;

namespace Astral.Network
{
	//public static class TestObject_StaticMethodss
	//{
	//	public static void TestFunction_Receive(Interfaces.IObject Instance, object[] Args)
	//	{
	//		((TestObject)Instance).TestFunction_Receive((int)Args[0], (string)Args[1]);
	//	}
	//
	//	public static void TestFunction2_Receive(Interfaces.IObject Instance, object[] Args)
	//	{
	//		((TestObject)Instance).TestFunction2_Receive((int)Args[0], (double)Args[1]);
	//	}
	//}
	//
	//public partial class TestObject : Interfaces.IObject
	//{
	//
	//	[Method(Flags = MFlags.Remote | MFlags.Reliable | MFlags.Ordered)]
	//	public void TestFunction_Receive(Int32 Pram1, string StringParam)
	//	{
	//	}
	//
	//	[Method(Flags = MFlags.Remote | MFlags.Reliable | MFlags.Ordered)]
	//	public void TestFunction2_Receive(Int32 Pram1, double StringParam)
	//	{
	//	}
	//
	//	//static TestObject()
	//	//{
	//	//	StaticInternal.StaticMethods[StaticObject<TestObject>.Index] = new List<Action<Object, object[]>>(50);
	//	//	StaticInternal.StaticMethods[StaticObject<TestObject>.Index][1] = //TestObject_StaticMethodss.TestFunction_Receive;
	//	//	StaticInternal.StaticMethods[StaticObject<TestObject>.Index][2] = //TestObject_StaticMethodss.TestFunction2_Receive;
	//	//}
	//}
}
