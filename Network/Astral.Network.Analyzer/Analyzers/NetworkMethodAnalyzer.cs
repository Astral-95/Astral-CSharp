using Astral.Analyzer.Entities;
using Astral.Network.Analyzer.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Astral.Network.Analyzer;

public static class NetworkMethodAnalyzer
{
    private static readonly HashSet<string> SupportedParamTypeNames = new()
        {
            "string",
            "byte[]",
            "Byte[]",
            "System.Byte[]",
            "System.Collections.Generic.List<string>",
            "System.Collections.Generic.List<byte>",
            "System.Collections.Generic.List<short>",
            "System.Collections.Generic.List<ushort>",
            "System.Collections.Generic.List<int>",
            "System.Collections.Generic.List<uint>",
            "System.Collections.Generic.List<long>",
            "System.Collections.Generic.List<ulong>",
            "System.Collections.Generic.List<float>",
            "System.Collections.Generic.List<double>",
            "Astral.Network.Interfaces.INetworkObject",
        };

    private static bool IsSupportedParamType(ITypeSymbol Type, Compilation Compilation)
    {
        // Allow primitive types (int, float, double, bool, etc.)
        if ((Type.IsValueType && Type.SpecialType != SpecialType.None))
        {
            return true;
        }

        if (SupportedParamTypeNames.Contains(Type.ToDisplayString()))
        {
            return true;
        }

        return false;
    }

    private static bool IsSupportedReturnType(ITypeSymbol type, Compilation Compilation)
    {
        if (type.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        return false;
    }

    private static bool ParametersMatch(ImmutableArray<IParameterSymbol> a, ImmutableArray<IParameterSymbol> b)
    {
        if (a.Length != b.Length) return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(a[i].Type, b[i].Type))
                return false;
        }

        return true;
    }

    public static bool Analyze(NetworkMethod Method, Compilation Compilation, SourceProductionContext Spc)
    {
        bool Valid = true;

        if (Method.Symbol.IsStatic || Method.Symbol.IsAbstract)
        {
            Spc.ReportDiagnostic(Diagnostic.Create(
                RemoteProceduralCallDiagnostics.StaticOrAbstract,
                Method.Symbol.Locations.FirstOrDefault(),
                Method.Symbol.Name
            ));
            Valid = false;
        }

        if (Method.Symbol.Name.EndsWith("_Receive") == false)
        {
            Spc.ReportDiagnostic(Diagnostic.Create(
                RemoteProceduralCallDiagnostics.IncorrectNameSuffix,
                Method.Symbol.Locations.FirstOrDefault(),
                Method.Symbol.Name,
                Method.Owner.Name
            ));

            Valid = false;
        }

        var ReservedImplName = Method.Symbol.Name + "_Send";

        var HasReservedImplemtation = Method.Owner.Symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Any(m => m.Name == ReservedImplName
                          && SymbolEqualityComparer.Default.Equals(m.ReturnType, Method.Symbol.ReturnType)
                          && ParametersMatch(m.Parameters, Method.Symbol.Parameters)
                          && SymbolEqualityComparer.Default.Equals(m, Method.Symbol));

        if (HasReservedImplemtation)
        {
            Spc.ReportDiagnostic(Diagnostic.Create(
                RemoteProceduralCallDiagnostics.ReservedSuffix,
                Method.Symbol.Locations.FirstOrDefault(),
                Method.Symbol.Name,
                Method.Owner.Name
            ));

            Valid = false;
        }

        // Parameters
        foreach (var Param in Method.Symbol.Parameters)
        {
            if (!IsSupportedParamType(Param.Type, Compilation))
            {
                var ParamSyntax = Param.DeclaringSyntaxReferences
                    .FirstOrDefault()?.GetSyntax() as ParameterSyntax;

                //Location TypeLocation = ParamSyntax.Type?.GetLocation();
                Location Location;

                if (ParamSyntax == null)
                {
                    Location = Param.Locations.FirstOrDefault();
                }
                else
                {
                    Location = ParamSyntax.Type?.GetLocation();
                }

                Spc.ReportDiagnostic(Diagnostic.Create(
                    RemoteProceduralCallDiagnostics.UnsupportedParameterType,
                    Location,
                    Method.Symbol.Name,
                    Param.Type.ToDisplayString()
                ));
                Valid = false;
            }
        }

        // Return type
        if (!IsSupportedReturnType(Method.Symbol.ReturnType, Compilation))
        {
            var MethodDeclaration = Method.Symbol.DeclaringSyntaxReferences
                    .FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
            var ReturnTypeLocation = MethodDeclaration.ReturnType.GetLocation();

            var diagnostic = Diagnostic.Create(
                RemoteProceduralCallDiagnostics.UnsupportedReturnType,
                ReturnTypeLocation,
                Method.Symbol.Name,
                Method.Symbol.ReturnType.ToDisplayString()
            );
            Spc.ReportDiagnostic(diagnostic);
            Valid = false;
        }

        return Valid;
    }


    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext Context)
    {
        var Invocation = (InvocationExpressionSyntax)Context.Node;

        var MethodSymbol = Context.SemanticModel.GetSymbolInfo(Invocation).Symbol as IMethodSymbol;
        if (MethodSymbol == null)
        {
            return;
        }

        // check if method belongs to a RemoteProceduralCall _Receive
        var ContainingMethod = MethodSymbol.ContainingType
            .GetMembers().OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Name.EndsWith("_Receive") &&
                                 m.GetAttributes().Any(a =>
                                     SymbolEqualityComparer.Default.Equals(
                                         a.AttributeClass,
                                         Context.Compilation.GetTypeByMetadataName("Astral.Attributes.Attributes.RemoteProceduralCallAttribute")
                                     )));

        if (ContainingMethod == null)
        {
            return;
        }

        // compute expected Send
        var BaseName = ContainingMethod.Name.Substring(0, ContainingMethod.Name.Length - "_Receive".Length);
        var SendMethodName = $"{BaseName}_Send";

        if (MethodSymbol.Name == SendMethodName)
        {
            var diagnostic = Diagnostic.Create(
                RemoteProceduralCallDiagnostics.Recursion,
                MethodSymbol.Locations.FirstOrDefault(),
                MethodSymbol.Name,
                MethodSymbol.ReturnType.ToDisplayString()
            );
        }
    }
}
