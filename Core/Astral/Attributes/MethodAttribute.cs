using System.Reflection;

namespace Astral.Attributes;

[Flags]
public enum MethodFlags
{
    None = 0,
    Remote = 1 << 0,
    Reliable = 1 << 1,
    Ordered = 1 << 2,
    Multicast = 1 << 3,
}

[AttributeUsage(AttributeTargets.Method)]
[System.Diagnostics.Conditional("COMPILE_TIME_ONLY")]
public class MethodAttribute : Attribute
{
    public string[] Tags { get; }
    public MethodAttribute(params string[] InTags) => Tags = InTags;

    public static void ValidateMethods(Assembly Assembly)
    {
        foreach (var type in Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<MethodAttribute>();
                if (attr == null) continue;

                foreach (var param in method.GetParameters())
                {
                    if (!IsSupportedType(param.ParameterType))
                        throw new Exception($"Method {method.Name} has unsupported parameter type {param.ParameterType}");
                }

                if (!IsSupportedType(method.ReturnType))
                    throw new Exception($"Method {method.Name} has unsupported return type {method.ReturnType}");
            }
        }
    }

    private static bool IsSupportedType(Type type)
    {
        // Allow primitive types (int, float, double, bool, etc.) and string
        if (type.IsPrimitive || type == typeof(string))
            return true;

        // Allow any class that implements ISerializable
        if (typeof(System.Runtime.Serialization.ISerializable).IsAssignableFrom(type))
            return true;

        return false;

        //return type == typeof(int) || type == typeof(string); // add whatever types your binary serializer supports
    }
}