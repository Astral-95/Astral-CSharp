namespace Astral.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
[System.Diagnostics.Conditional("COMPILE_TIME_ONLY")]
public class AstralAttribute : Attribute
{
    public string? ConstructorCode { get; }
}