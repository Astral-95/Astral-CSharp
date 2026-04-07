using Astral.Interfaces;
using Astral.Serialization;
using System.Collections.Concurrent;

namespace Astral.Tests.Tests;

//public partial class TestObject : IObject
//{
//    IObject? PrivateOuter = null;
//    public IObject? Outer { get => PrivateOuter; }
//
//    UInt32 PrivateObjectId { get; set; } = 0;
//    public UInt32 ObjectId { get => PrivateObjectId; }
//
//    Int32 PrivateObjectIndex = -1;
//    public Int32 ObjectIndex { get => PrivateObjectIndex; }
//
//    Int32 NextFreeDefaultSubobjectIndex { get; set; } = 2;
//    ConcurrentStack<Int32> FreeDefaultIndices = new ConcurrentStack<Int32>();
//
//
//    private readonly List<IObject> PrivateDefaultSubobjects = new List<IObject?>();
//    public IReadOnlyList<IObject> DefaultSubobjects => PrivateDefaultSubobjects;
//
//
//    public void SetOuter(IObject NewOuter)
//    {
//        PrivateOuter = NewOuter;
//    }
//
//    public void SetObjectId(UInt32 ObjectId)
//    {
//        PrivateObjectId = ObjectId;
//    }
//
//
//    // Call "Parent" method when you override this.
//    public virtual void CreatedAsSubobject(IObject Outer, Int32 ObjectIndex)
//    {
//        PrivateOuter = Outer;
//        PrivateObjectIndex = ObjectIndex;
//    }
//
//    // Call "Parent" method when you override this.
//    public virtual void AddDefualtSubobject(IObject DefualtSubobject)
//    {
//        if (DefualtSubobject.ObjectIndex < 2) throw new InvalidOperationException("Target object is already registered as subobject.");
//
//        if (DefaultSubobjects.Count < 2)
//        {
//            PrivateDefaultSubobjects.Add(DefualtSubobject);
//            DefualtSubobject.CreatedAsSubobject(this, 1);
//            return;
//        }
//
//        if (FreeDefaultIndices.TryPop(out Int32 DefIndex))
//        {
//            PrivateDefaultSubobjects[DefIndex] = DefualtSubobject;
//            DefualtSubobject.CreatedAsSubobject(this, DefIndex);
//            return;
//        }
//
//        DefIndex = NextFreeDefaultSubobjectIndex++;
//
//        PrivateDefaultSubobjects.Add(DefualtSubobject);
//        DefualtSubobject.CreatedAsSubobject(this, DefIndex);
//    }
//}
//
//
//abstract partial class AbstractTestObject : IObject
//{
//    public abstract IObject? Outer { get; }
//    public abstract uint ObjectId { get; }
//    public abstract int ObjectIndex { get; }
//    public abstract IReadOnlyList<IObject> DefaultSubobjects { get; }
//
//    public abstract void AddDefualtSubobject(IObject DefualtSubobject);
//    public abstract void CreatedAsSubobject(IObject Outer, int ObjectIndex);
//    public abstract ulong GetTypeHash();
//    public abstract int GetTypeIndex();
//    public abstract void InvokeMethod(int MethodIndex, ByteReader? Reader = null);
//    public abstract void SetObjectId(uint ObjectId);
//    public abstract void SetOuter(IObject NewOuter);
//}


public partial class TestObject : IObject
{
}


abstract partial class AbstractTestObject : IObject
{
}