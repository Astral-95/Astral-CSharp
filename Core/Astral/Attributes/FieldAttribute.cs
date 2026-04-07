namespace Astral.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
[System.Diagnostics.Conditional("COMPILE_TIME_ONLY")]
public class FieldAttribute : Attribute
{
    public string[] Tags { get; }
    public FieldAttribute(params string[] InTags) => Tags = InTags;
}