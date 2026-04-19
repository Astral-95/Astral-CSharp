using System.Linq.Expressions;
using System.Reflection;

namespace Astral.Tools;

public sealed class WeakAction
{
    static ulong PrivateNextId = 0;
    public ulong Id { get; private set; }

    public delegate void WeakActionDelegate(object Target);

    public WeakReference TargetRef;
    public WeakActionDelegate Invoker;
    public MethodInfo Method;

    public WeakAction(Action Action)
    {
        if (Action == null) throw new ArgumentNullException(nameof(Action));
        if (Action.Target == null) throw new ArgumentException("Action.Target cannot be null for instance methods.");

        TargetRef = new WeakReference(Action.Target);
        Method = Action.Method;

        // Build expression tree: (object target) => ((TargetType)target).Method()
        var targetParam = Expression.Parameter(typeof(object), "target");
        var castTarget = Expression.Convert(targetParam, Action.Target.GetType());
        var call = Expression.Call(castTarget, Method);

        Invoker = Expression.Lambda<WeakActionDelegate>(call, targetParam).Compile();

        Id = NextId;
    }

    public bool IsValid { get => TargetRef.Target != null; }
    public object? Target => TargetRef.Target;
    static internal ulong NextId { get => Interlocked.Increment(ref PrivateNextId); }

    public void Invoke()
    {
        Invoker(TargetRef.Target!);
    }
    public bool TryInvoke()
    {
        var Instance = TargetRef.Target;
        if (Instance == null)
            return false;

        Invoker(Instance);
        return true;
    }

    public bool Equals(WeakAction? other)
    {
        if (other == null) return false;

        var t1 = TargetRef.Target;
        var t2 = other.TargetRef.Target;

        // If either target has been collected, treat as not equal
        if (t1 == null || t2 == null) return false;

        return t1 == t2 && Method == other.Method;
    }

    public override bool Equals(object? obj)
    {
        // Avoid infinite recursion: check type explicitly
        return obj is WeakAction other && Equals(other);
    }

    public static bool operator ==(WeakAction? Left, object? Right)
    {
        if (Left is null) return Right is null;
        return Left.Equals(Right);
    }

    public static bool operator !=(WeakAction? Left, object? Right)
    {
        return !(Left == Right);
    }

    public override int GetHashCode()
    {
        var Instance = TargetRef.Target;

        unchecked
        {
            int Hash = 17;
            Hash = Hash * 31 + (Instance?.GetHashCode() ?? 0);
            Hash = Hash * 31 + Method.GetHashCode();
            return Hash;

        }
    }
}

public sealed class WeakAction<T>
{
    public delegate void WeakActionDelegate(T Arg1);
    public ulong Id { get; private set; }

    public WeakReference TargetRef;
    private readonly Action<object, T> Invoker;
    public MethodInfo Method;

    public WeakAction(Action<T> Action)
    {
        if (Action == null) throw new ArgumentNullException(nameof(Action));
        if (Action.Target == null) throw new ArgumentException("Action.Target cannot be null for instance methods.");

        TargetRef = new WeakReference(Action.Target);
        Method = Action.Method;

        // Build expression tree: (object target, T1 arg1, T2 arg2) => ((TargetType)target).Method(arg1, arg2)
        var targetParam = Expression.Parameter(typeof(object), "target");
        var arg1Param = Expression.Parameter(typeof(T), "arg1");

        var castTarget = Expression.Convert(targetParam, Action.Target.GetType());
        var call = Expression.Call(castTarget, Method, arg1Param);

        Invoker = Expression.Lambda<Action<object, T>>(call, targetParam, arg1Param).Compile();

        Id = WeakAction.NextId;
    }

    public bool IsValid { get => TargetRef.Target != null; }
    public object? Target => TargetRef.Target;

    public void Invoke(T Arg)
    {
        Invoker(TargetRef.Target!, Arg);
    }
    public bool TryInvoke(T Arg)
    {
        var Instance = TargetRef.Target;
        if (Instance == null)
            return false;

        Invoker(Instance, Arg);
        return true;
    }

    public bool Equals(WeakAction<T>? other)
    {
        if (other == null) return false;

        var t1 = TargetRef.Target;
        var t2 = other.TargetRef.Target;

        // If either target has been collected, treat as not equal
        if (t1 == null || t2 == null) return false;

        return t1 == t2 && Method == other.Method;
    }

    public override bool Equals(object? obj)
    {
        // Avoid infinite recursion: check type explicitly
        return obj is WeakAction<T> other && Equals(other);
    }

    public static bool operator ==(WeakAction<T>? Left, object? Right)
    {
        if (Left is null) return Right is null;
        return Left.Equals(Right);
    }

    public static bool operator !=(WeakAction<T>? Left, object? Right)
    {
        return !(Left == Right);
    }

    public override int GetHashCode()
    {
        var Instance = TargetRef.Target;

        unchecked
        {
            int Hash = 17;
            Hash = Hash * 31 + (Instance?.GetHashCode() ?? 0);
            Hash = Hash * 31 + Method.GetHashCode();
            return Hash;
        }
    }
}

public sealed class WeakAction<T1, T2>
{
    public delegate void WeakActionDelegate(T1 Arg1, T2 Arg2);

    public ulong Id { get; private set; }
    public WeakReference TargetRef;
    private readonly Action<object, T1, T2> Invoker;
    public MethodInfo Method;

    public WeakAction(Action<T1, T2> Action)
    {
        if (Action == null) throw new ArgumentNullException(nameof(Action));
        if (Action.Target == null) throw new ArgumentException("Action.Target cannot be null for instance methods.");

        TargetRef = new WeakReference(Action.Target);
        Method = Action.Method;

        // Build expression tree: (object target, T1 arg1, T2 arg2) => ((TargetType)target).Method(arg1, arg2)
        var targetParam = Expression.Parameter(typeof(object), "target");
        var arg1Param = Expression.Parameter(typeof(T1), "arg1");
        var arg2Param = Expression.Parameter(typeof(T2), "arg2");

        var castTarget = Expression.Convert(targetParam, Action.Target.GetType());
        var call = Expression.Call(castTarget, Method, arg1Param, arg2Param);

        Invoker = Expression.Lambda<Action<object, T1, T2>>(call, targetParam, arg1Param, arg2Param).Compile();

        Id = WeakAction.NextId;
    }

    public bool IsValid { get => TargetRef.Target != null; }
    public object? Target => TargetRef.Target;

    public void Invoke(T1 Arg1, T2 Arg2)
    {
        Invoker(Target!, Arg1, Arg2);
    }
    public bool TryInvoke(T1 Arg1, T2 Arg2)
    {
        var Instance = TargetRef.Target;
        if (Instance == null)
            return false;

        Invoker(Instance, Arg1, Arg2);
        return true;
    }

    public bool Equals(WeakAction<T1, T2>? Other)
    {
        if (Other == null || Target == null || Other.Target == null) return false;

        return Other.Target == Target && Method == Other.Method;
    }

    public override bool Equals(object? Obj)
    {
        return Obj is WeakAction<T1, T2> Other && Equals(Other);
    }

    public static bool operator ==(WeakAction<T1, T2>? Left, object? Right)
    {
        if (Left is null) return Right is null;
        return Left.Equals(Right);
    }

    public static bool operator !=(WeakAction<T1, T2>? Left, object? Right)
    {
        return !(Left == Right);
    }

    public override int GetHashCode()
    {
        var Instance = TargetRef.Target;

        unchecked
        {
            int Hash = 17;
            Hash = Hash * 31 + (Instance?.GetHashCode() ?? 0);
            Hash = Hash * 31 + Method.GetHashCode();
            return Hash;
        }
    }
}

public sealed class WeakAction<T1, T2, T3>
{
    public ulong Id { get; private set; }
    public WeakReference TargetRef;
    private readonly Action<object, T1, T2, T3> Invoker;
    public MethodInfo Method;

    public WeakAction(Action<T1, T2> Action)
    {
        if (Action == null) throw new ArgumentNullException(nameof(Action));
        if (Action.Target == null) throw new ArgumentException("Action.Target cannot be null for instance methods.");

        TargetRef = new WeakReference(Action.Target);
        Method = Action.Method;

        // Build expression tree: (object target, T1 arg1, T2 arg2) => ((TargetType)target).Method(arg1, arg2)
        var targetParam = Expression.Parameter(typeof(object), "target");
        var arg1Param = Expression.Parameter(typeof(T1), "arg1");
        var arg2Param = Expression.Parameter(typeof(T2), "arg2");
        var arg3Param = Expression.Parameter(typeof(T3), "arg3");

        var castTarget = Expression.Convert(targetParam, Action.Target.GetType());
        var call = Expression.Call(castTarget, Method, arg1Param, arg2Param, arg3Param);

        Invoker = Expression.Lambda<Action<object, T1, T2, T3>>(call, targetParam, arg1Param, arg2Param, arg3Param).Compile();
        Id = WeakAction.NextId;
    }

    public bool IsValid { get => TargetRef.Target != null; }
    public object? Target => TargetRef.Target;

    public void Invoke(T1 Arg1, T2 Arg2, T3 Arg3)
    {
        Invoker(TargetRef.Target!, Arg1, Arg2, Arg3);
    }
    public bool TryInvoke(T1 Arg1, T2 Arg2, T3 Arg3)
    {
        var Instance = TargetRef.Target;
        if (Instance == null)
            return false;

        Invoker(Instance, Arg1, Arg2, Arg3);
        return true;
    }

    public bool Equals(WeakAction<T1, T2, T3>? other)
    {
        if (other == null) return false;

        var t1 = TargetRef.Target;
        var t2 = other.TargetRef.Target;

        // If either target has been collected, treat as not equal
        if (t1 == null || t2 == null) return false;

        return t1 == t2 && Method == other.Method;
    }

    public override bool Equals(object? obj)
    {
        // Avoid infinite recursion: check type explicitly
        return obj is WeakAction<T1, T2, T3> other && Equals(other);
    }

    public static bool operator ==(WeakAction<T1, T2, T3>? Left, object? Right)
    {
        if (Left is null) return Right is null;
        return Left.Equals(Right);
    }

    public static bool operator !=(WeakAction<T1, T2, T3>? Left, object? Right)
    {
        return !(Left == Right);
    }

    public override int GetHashCode()
    {
        var Instance = TargetRef.Target;

        unchecked
        {
            int Hash = 17;
            Hash = Hash * 31 + (Instance?.GetHashCode() ?? 0);
            Hash = Hash * 31 + Method.GetHashCode();
            return Hash;
        }
    }
}

public sealed class WeakAction<T1, T2, T3, T4>
{
    public ulong Id { get; private set; }
    public WeakReference TargetRef;
    private readonly Action<object, T1, T2, T3, T4> Invoker;
    public MethodInfo Method;

    public WeakAction(Action<T1, T2> Action)
    {
        if (Action == null) throw new ArgumentNullException(nameof(Action));
        if (Action.Target == null) throw new ArgumentException("Action.Target cannot be null for instance methods.");

        TargetRef = new WeakReference(Action.Target);
        Method = Action.Method;

        // Build expression tree: (object target, T1 arg1, T2 arg2) => ((TargetType)target).Method(arg1, arg2)
        var targetParam = Expression.Parameter(typeof(object), "target");
        var arg1Param = Expression.Parameter(typeof(T1), "arg1");
        var arg2Param = Expression.Parameter(typeof(T2), "arg2");
        var arg3Param = Expression.Parameter(typeof(T3), "arg3");
        var arg4Param = Expression.Parameter(typeof(T4), "arg4");

        var castTarget = Expression.Convert(targetParam, Action.Target.GetType());
        var call = Expression.Call(castTarget, Method, arg1Param, arg2Param, arg3Param, arg4Param);

        Invoker = Expression.Lambda<Action<object, T1, T2, T3, T4>>(call, targetParam, arg1Param, arg2Param, arg3Param, arg4Param).Compile();
        Id = WeakAction.NextId;
    }

    public bool IsValid { get => TargetRef.Target != null; }
    public object? Target => TargetRef.Target;

    public void Invoke(T1 Arg1, T2 Arg2, T3 Arg3, T4 Arg4)
    {
        Invoker(TargetRef.Target!, Arg1, Arg2, Arg3, Arg4);
    }
    public bool TryInvoke(T1 Arg1, T2 Arg2, T3 Arg3, T4 Arg4)
    {
        var Instance = TargetRef.Target;
        if (Instance == null)
            return false;

        Invoker(Instance, Arg1, Arg2, Arg3, Arg4);
        return true;
    }

    public bool Equals(WeakAction<T1, T2, T3, T4>? other)
    {
        if (other == null) return false;

        var t1 = TargetRef.Target;
        var t2 = other.TargetRef.Target;

        // If either target has been collected, treat as not equal
        if (t1 == null || t2 == null) return false;

        return t1 == t2 && Method == other.Method;
    }

    public override bool Equals(object? obj)
    {
        // Avoid infinite recursion: check type explicitly
        return obj is WeakAction<T1, T2, T3, T4> other && Equals(other);
    }

    public static bool operator ==(WeakAction<T1, T2, T3, T4>? Left, object? Right)
    {
        if (Left is null) return Right is null;
        return Left.Equals(Right);
    }

    public static bool operator !=(WeakAction<T1, T2, T3, T4>? Left, object? Right)
    {
        return !(Left == Right);
    }

    public override int GetHashCode()
    {
        var Instance = TargetRef.Target;

        unchecked
        {
            int Hash = 17;
            Hash = Hash * 31 + (Instance?.GetHashCode() ?? 0);
            Hash = Hash * 31 + Method.GetHashCode();
            return Hash;
        }
    }
}