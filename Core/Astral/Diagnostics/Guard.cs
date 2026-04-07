using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Astral.Diagnostics;

public static class Guard
{
    [Conditional("CFG_TRACE")]
    public static void TraceAssert([DoesNotReturnIf(false)] bool Condition, string Message = "")
    {
        if (Condition) return;

        Environment.FailFast($"Assertion failed: {Message}\n{new StackTrace(1, true)}");
    }

    [Conditional("CFG_DEBUG")]
    public static void DebugAssert([DoesNotReturnIf(false)] bool Condition, string Message = "")
    {
        if (Condition) return;

        Environment.FailFast($"Assertion failed: {Message}\n{new StackTrace(1, true)}");
    }

    public static void Assert([DoesNotReturnIf(false)] bool Condition, string Message = "")
    {
        if (Condition) return;

        Environment.FailFast($"Assertion failed: {Message}\n{new StackTrace(1, true)}");
    }



    [Conditional("CFG_TRACE")]
    [DoesNotReturn]
    public static void TraceFail(string Message)
    {
        var Stack = new StackTrace(1, true);
        Environment.FailFast($"{Message}\n{Stack}");
    }

    [Conditional("CFG_DEBUG")]
    [DoesNotReturn]
    public static void DebugFail(string Message)
    {
        var Stack = new StackTrace(1, true);
        Environment.FailFast($"{Message}\n{Stack}");
    }

    [DoesNotReturn]
    public static Exception Fail(string Message)
    {
        var Stack = new StackTrace(1, true);
        Environment.FailFast($"{Message}\n{Stack}"); return null!;
    }
}