using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Linq;

namespace Astral.Analyzer.Helpers;

public class AstAnalyzerHelpers
{
    public static void AnalyzeClass(AnalysisContext Context, SymbolAnalysisContext AnalysisContext, Compilation Compilation)
    {
        var ClassSymbol = (INamedTypeSymbol)AnalysisContext.Symbol;

        if (ClassSymbol.TypeKind != TypeKind.Class)
        {
            return;
        }

        var ObjectSymbol = Compilation.GetTypeByMetadataName("Astral.Network.NetworkObject");
        var AstralAttribute = Compilation.GetTypeByMetadataName("Astral.Attributes.AstralAttribute");

        if (ObjectSymbol == null && ClassSymbol.ToDisplayString() != "Astral.Network.NetworkObject")
        {
            return;
        }

        if (!IsOrSubclassOf(ClassSymbol, ObjectSymbol))
        {
            return;
        }

        AnalyzeRemoteCalls(AnalysisContext, ClassSymbol);
    }

    public static bool IsOrSubclassOf(INamedTypeSymbol ClassSymbol, INamedTypeSymbol BaseType)
    {
        for (var Current = ClassSymbol; Current != null; Current = Current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(Current, BaseType))
                return true;
        }
        return false;
    }

    public static void AnalyzeRemoteCalls(SymbolAnalysisContext AnalysisContext, INamedTypeSymbol ClassSymbol)
    {
        var ClassMethods = ClassSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .ToList();

        //AnalysisContext.ReportDiagnostic(Diagnostic.Create(
        //			DebugDiagnostics.Info,
        //			ClassSymbol.Locations.FirstOrDefault(),
        //			$"Analyzing [{ClassSymbol.ToDisplayString()}] NumMethods: [{ClassSymbol.GetMembers//().OfType<IMethodSymbol>().Count()}] NumFilteredMethods: [{ClassMethods.Count()}]"
        //		));

        foreach (var Method in ClassMethods)
        {
            //AstAnalyzerRemoteCalls.AnalyzeMethod(AnalysisContext, ClassSymbol, Method);
        }
    }
}