using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Linq;

namespace Astral.Analyzer.Utils;

public class DiagnosticHelper
{
    public static ImmutableArray<DiagnosticDescriptor> GetAllDiagnostics<T>()
    {
        return typeof(T)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToImmutableArray();
    }
}