using Astral.Analyzer.Entities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Astral.Analyzer.Generators;

class Core
{
    public Compilation Compilation;
    public INamedTypeSymbol CoreInterface;
    public INamedTypeSymbol NetworkInterface;
    public INamedTypeSymbol AstralAttribute;
}
[Generator]
public class Generator : IIncrementalGenerator
{
    static int NextTypeIndex { get; set; } = 0;
    static ConfigBlock Configuration { get; set; }
    static string TypeFullName { get; set; }
    static string AstClassCode { get; set; } = "";
    static string PartialClassCode { get; set; } = "";

    static List<string> MetaClassList = [];

    public void Initialize(IncrementalGeneratorInitializationContext Context)
    {
        var ConfigProvider =
            Context.AdditionalTextsProvider
                .Where(File => Path.GetFileName(File.Path) == "AstralGenConfig.txt")
                .Select((File, Ct) => File.GetText(Ct)?.ToString())
                .Collect()
                .Select((Files, _) =>
                {
                    if (Files.Length == 0)
                        return (ConfigBlock?)null;

                    if (Files.Length > 1)
                        throw new InvalidOperationException(
                            "Multiple AstralGenConfig.txt files found");

                    return ConfigBlock.ParseFile(Files[0]!);
                });

        var CoreTypeSymbols = Context.CompilationProvider
            .Select((compilation, _) =>
            {
                var CoreInterface = compilation.GetTypeByMetadataName("Astral.Interfaces.IObject");
                var NetworkInterface = compilation.GetTypeByMetadataName("Astral.Network.Interfaces.INetworkObject");
                var AstralAttribute = compilation.GetTypeByMetadataName("Astral.Attributes.AstralAttribute");

                return new Core
                {
                    Compilation = compilation,
                    CoreInterface = CoreInterface,
                    NetworkInterface = NetworkInterface,
                    AstralAttribute = AstralAttribute
                };
            });

        var ClassSymbolsProvider = Context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var syntax = (ClassDeclarationSyntax)ctx.Node;
                    return ctx.SemanticModel.GetDeclaredSymbol(syntax) as INamedTypeSymbol;
                })
            .Where(symbol => symbol is not null)!
            .Select((symbol, _) => (INamedTypeSymbol)symbol!);


        var ClassSymbolWithCore = ClassSymbolsProvider
            .Collect()
            .Select((AllSymbols, _) => AllSymbols
                .GroupBy(s => s, SymbolEqualityComparer.Default).Select(g => g.First()).ToImmutableArray()
            ).SelectMany<ImmutableArray<INamedTypeSymbol>, INamedTypeSymbol>((all, ct) => all).Combine(CoreTypeSymbols);


        Context.RegisterSourceOutput(ClassSymbolWithCore, (Spc, Source) =>
        {
            try
            {
                var (ClassSymbol, Core) = Source;

                ProcessClassSymbol(Spc, Core, ClassSymbol);
            }
            catch (System.Exception Ex)
            {
                Spc.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("AST999", "Exception", Ex.ToString(), "Astral", DiagnosticSeverity.Error, true),
                    Location.None));
            }
            finally
            {
                Spc.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("AST998", "Info", "Run complete", "Astral", DiagnosticSeverity.Info, true),
                    Location.None));
            }
        });
    }


    void ProcessClassSymbol(SourceProductionContext Spc, Core Core, INamedTypeSymbol ClassSymbol)
    {
        if (!ClassSymbol.AllInterfaces.Contains(Core.CoreInterface)) return;

        //Spc.ReportDiagnostic
        //	(
        //	Diagnostic.Create(DebugDiagnostics.Info,
        //	ClassSymbol.Locations[0],
        //	$"Class {ClassSymbol.Name}")
        //	);

        try
        {
            TypeFullName = ClassSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
        catch (Exception Ex)
        {
            ReportException(Spc, "ToDisplayString ex", Ex);
            return;
        }

        Class Class = new(Core.Compilation, Spc, ClassSymbol, Core.CoreInterface, Core.NetworkInterface);

        var GeneratedFileName = $"{ClassSymbol.Name}.gen.cs";
        Spc.AddSource(GeneratedFileName, SourceText.From(Class.Code, Encoding.UTF8));
    }

    bool IsOrSubclassOf(INamedTypeSymbol Type, INamedTypeSymbol BaseType)
    {
        for (var Current = Type; Current != null; Current = Current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(Current, BaseType))
                return true;
        }
        return false;
    }

    private static bool HasAstralAttribute(SyntaxNode Node, CancellationToken _)
    {
        if (Node is ClassDeclarationSyntax Cls)
        {
            bool bHasAstralAttribute = Cls.AttributeLists.SelectMany(al => al.Attributes)
                .Any(Attr => Attr.Name.ToString() == "Astral" || Attr.Name.ToString() == "AstralAttribute");

            if (bHasAstralAttribute == false)
            {
                return false;
            }

            //return Cls.Members.OfType<MethodDeclarationSyntax>().Any(m => m.AttributeLists.Count > 0);
            return true;
        }

        return false;
    }

    static bool HasAttributeIncludingBase(INamedTypeSymbol Symbol, INamedTypeSymbol AttributeType)
    {
        for (var Current = Symbol; Current != null; Current = Current.BaseType)
        {
            if (Current.GetAttributes().Any(A => A.AttributeClass.Name == "Astral" || A.AttributeClass.Name == "AstralAttribute"))
                return true;
        }
        return false;
    }

    private static ClassDeclarationSyntax GetClassDeclaration(GeneratorSyntaxContext context, CancellationToken _)
    {
        return context.Node as ClassDeclarationSyntax;
    }


    private static UInt64 ComputeTypeHash(ISymbol Symbol)
    {
        // Fully qualified metadata name (deterministic across builds)
        string MetadataName = Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        using var Sha1 = System.Security.Cryptography.SHA1.Create();
        byte[] Bytes = Encoding.UTF8.GetBytes(MetadataName);
        byte[] Hash = Sha1.ComputeHash(Bytes);
        return BitConverter.ToUInt64(Hash, 0);
    }

    private static void GenerateTypeRegisteryCode(SourceProductionContext Spc)
    {
        string Entries = string.Join(",\n            ", MetaClassList);
        string Code = $@"
namespace Astral.Generated
{{
	public static class TypeRegistry
	{{
		public static readonly Astral.Factories.TypeData[] Registry = new Astral.Factories.TypeData[]
	    {{
	        {Entries}
	    }};
	
		public static Astral.Factories.TypeData GetMetadata(Int32 TypeIndex) => Registry[TypeIndex];
	    public static object CreateInstance(Int32 TypeIndex) => Registry[TypeIndex].Constructor();
	}}
}}";
        var GeneratedFileName = $"TypeRegistry.gen.cs";
        Spc.AddSource(GeneratedFileName, SourceText.From(Code, Encoding.UTF8));
    }







    internal static void ReportException(SourceProductionContext Spc, string Step, Exception Ex)
    {
        Spc.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor("AST1", "Exception", $"{Step} failed: {Ex}", "Astral", DiagnosticSeverity.Error, true),
            Location.None));
    }

    internal static void ReportError(SourceProductionContext Spc, string Error)
    {
        Spc.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor("AST2", "Error", $"Parser error: {Error}", "Astral", DiagnosticSeverity.Error, true),
            Location.None));
    }

    internal static void ReportWarn(SourceProductionContext Spc, string Message)
    {
        Spc.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor("AST3", "Warning", Message, "Astral", DiagnosticSeverity.Warning, true),
            Location.None));
    }
}


#if false
			string Code = $@"using {ClassSymbol.ContainingNamespace.ToDisplayString()};
namespace {ClassSymbol.ContainingNamespace.ToDisplayString()}
{{
	public static class {ClassName}_StaticMethods{GenericParams + Constraints}
	{{
		{StaticMethodsCode}
	}}
	
    public partial class {ClassNameWithParams}
    {{
		//public static StaticObject StaticObject()
		//{{
		//    return StaticObject<{ClassNameWithParams}>;
		//}}

		public {VirtOveride} int GetTypeIndex() => Astral.StaticObject<{ClassNameWithParams}>.Index;
		public static int GetTypeIndexStatic() => Astral.StaticObject<{ClassNameWithParams}>.Index;

		static {ClassSymbol.Name}()
		{{
			Astral.ObjectFactory.RegisterConstructor<{ClassNameWithParams}>(Constructor: () => new {ClassNameWithParams}());
			//Class = Astral.Class.Register(() => new {ClassSymbol.Name}());
			{MethodArrayCode}
		}}
		{(ClassSymbol.Constructors.Any(ctor => ctor.Parameters.Length == 0) ? "" : $@"
		private {ClassSymbol.Name}() {{}}")}
		{MethodsCode}
    }}
}}";
#endif