using Astral.Interfaces;
using System.Collections.Concurrent;

namespace Astral;

public static class StaticObject<T> where T : Interfaces.IObject
{
    public static int Index = 0;
}

public class ObjectsManager
{
    static object LockObject = new();
    private static int NextTypeIndex = 0;
    private static List<Interfaces.IObject?> DefaultObjects = new(1024);
    private static Dictionary<UInt64, Int32> HashIndexMap = new(1024);
    private static List<Func<Interfaces.IObject>?> ObjectConstructors = new(1024);
    private static ConcurrentDictionary<string, Func<Interfaces.IObject>?> ObjectConstructorsByName = new();

#pragma warning disable CS8618
    private protected ObjectsManager() { }
#pragma warning restore CS8618

    static ObjectsManager()
    {
        DefaultObjects = Enumerable.Repeat<IObject?>(null, 32000).ToList();
        ObjectConstructors = Enumerable.Repeat<Func<IObject>?>(null, 32000).ToList();
    }
    public static void Register<T>(Func<Interfaces.IObject> Constructor, string FullName, UInt64 TypeHash) where T : Interfaces.IObject
    {
        int Index = NextTypeIndex++;

        if (ObjectConstructors.Count < Index)
            ObjectConstructors.EnsureCapacity(Index + 1);
        if (DefaultObjects.Count <= Index)
            DefaultObjects.EnsureCapacity(Index + 1);

        StaticObject<T>.Index = Index;

        HashIndexMap.Add(TypeHash, Index);
        ObjectConstructors[Index] = Constructor;
        ObjectConstructorsByName.TryAdd(FullName, Constructor);
    }

    public static Int32 HashToIndex(UInt64 Hash)
    {
        return HashIndexMap[Hash];
    }

    public static Interfaces.IObject GetOrCreateDefaultObject(int Index)
    {
        Interfaces.IObject Result;
        lock (LockObject)
        {
            var DefObj = DefaultObjects[Index];

            if (DefObj != null)
            {
                return DefObj;
            }

            Result = DefaultObjects[Index] = ObjectConstructors[Index]!();
        }
        return Result;
    }
    public static Interfaces.IObject CreateInstance(int Index)
    {
        Interfaces.IObject NewInstance;
        lock (LockObject)
        {
            NewInstance = ObjectConstructors[Index]!();
        }
        return NewInstance;
    }

    public static Interfaces.IObject CreateInstance(string FullName)
    {
        Interfaces.IObject NewInstance;
        lock (LockObject)
        {
            NewInstance = ObjectConstructorsByName[FullName]!();
            //NewInstance.DefaultObject = GetOrCreateDefaultObject(NewInstance.GetTypeIndex());
        }
        return NewInstance;
    }

    public static T CreateInstance<T>(int Index) where T : Interfaces.IObject
    {
        Interfaces.IObject NewInstance;
        lock (LockObject)
        {
            NewInstance = ObjectConstructors[Index]!();
            //NewInstance.DefaultObject = GetOrCreateDefaultObject(Index);
        }
        return (T)NewInstance;
    }
}