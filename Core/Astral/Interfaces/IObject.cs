using Astral.Serialization;

namespace Astral.Interfaces;

[Flags]
public enum IObjectFlags : byte
{
    None = 0,
    Disabled = 1 << 0,
    Destroyed = 1 << 1,
}
public interface IObject
{
    public IObject? Outer { get; }
    public UInt32 ObjectId { get; }
    public Int32 ObjectIndex { get; }
    public IReadOnlyList<IObject> DefaultSubobjects { get; }

    public int GetTypeIndex();
    public UInt64 GetTypeHash();


    public void SetObjectId(UInt32 ObjectId);

    public void SetOuter(IObject NewOuter);

    public void InvokeMethod(int MethodIndex, ByteReader? Reader = null);


    public void CreatedAsSubobject(IObject Outer, Int32 ObjectIndex);
    public void AddDefualtSubobject(IObject DefualtSubobject);

}