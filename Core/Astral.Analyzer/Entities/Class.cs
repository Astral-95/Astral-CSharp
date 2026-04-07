using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using System.Text;

namespace Astral.Analyzer.Entities;

public class Class
{
    SourceProductionContext Spc;
    public INamedTypeSymbol Symbol;
    public INamedTypeSymbol CoreInterface;
    public INamedTypeSymbol NetworkInterface;

    public UInt64 Hash { get; private set; }
    public string Name { get; private set; }
    public string FullName { get; private set; }
    public string GenericParams { get; private set; } = "";
    public string BaseClassName { get; private set; } = "";
    public string NameWithParams { get; private set; } = "";
    public string NameGenericBaseParamConstrains { get; private set; } = "";
    public string AccessModifierStr { get; private set; } = "";
    public string OverridingModifiersStr { get; private set; } = "";

    //bool IsObject = false;
    public bool IsBase { get; private set; }
    public bool NonAbstractOuterImplemented { get; private set; }


    public string Code { get; private set; } = "";

    public Class(Compilation Compilation, SourceProductionContext Spc, INamedTypeSymbol Symbol, INamedTypeSymbol CoreInterface, INamedTypeSymbol NetworkInterface)
    {
        //Spc.ReportDiagnostic
        //    (
        //    Diagnostic.Create(DebugDiagnostics.Info,
        //    Symbol.Locations[0],
        //    $"Class {Symbol.Name}")
        //    );

        this.Spc = Spc;
        this.Symbol = Symbol;
        this.CoreInterface = CoreInterface;
        this.NetworkInterface = NetworkInterface;

        Hash = ComputeTypeHash(Symbol);
        Name = Symbol.Name;
        //IsObject = Name == "Object";
        FullName = Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var BaseType = Symbol.BaseType;
        IsBase = BaseType == null || !BaseType.AllInterfaces.Contains(CoreInterface, SymbolEqualityComparer.Default);

        NonAbstractOuterImplemented = HasNonAbstractParentWithCoreInterface();

        if (!IsBase)
        {
            BaseClassName = " : " + Symbol.BaseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        if (Symbol.TypeParameters.Length > 0)
        {
            GenericParams = "<" + string.Join(", ", Symbol.TypeParameters.Select(tp => tp.Name)) + ">";
        }

        var ConstraintsSb = new StringBuilder();
        foreach (var TypeParam in Symbol.TypeParameters)
        {
            var Cstrn = new System.Collections.Generic.List<string>();

            if (TypeParam.HasReferenceTypeConstraint)
                Cstrn.Add("class");
            if (TypeParam.HasValueTypeConstraint)
                Cstrn.Add("struct");
            foreach (var ConstraintType in TypeParam.ConstraintTypes)
                Cstrn.Add(ConstraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            if (TypeParam.HasConstructorConstraint)
                Cstrn.Add("new()");

            if (Cstrn.Count > 0)
            {
                ConstraintsSb.AppendLine();
                ConstraintsSb.Append("    where ");
                ConstraintsSb.Append(TypeParam.Name);
                ConstraintsSb.Append(" : ");
                ConstraintsSb.Append(string.Join(", ", Cstrn));
            }
        }
        var Constraints = ConstraintsSb.ToString();


        // ClassName<Param1, Param2> or ClassName if no params
        NameWithParams = Name + GenericParams;
        NameGenericBaseParamConstrains = Name + GenericParams + BaseClassName + Constraints;
        OverridingModifiersStr = Name == "Object" ? "virtual" : "override";
        //OverridingModifiersStr = IsBase ? "" : "override";
        AccessModifierStr = Symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => ""
        };
        GenerateCode();
    }


    bool HasNonAbstractParentWithCoreInterface()
    {
        var Current = Symbol.BaseType;

        while (Current != null)
        {
            // Stop if this base class does NOT implement the interface
            if (!Current.AllInterfaces.Contains(CoreInterface, SymbolEqualityComparer.Default))
                return false;

            // If this base class is concrete (non-abstract), return true
            if (!Current.IsAbstract)
                return true;

            // Move up the chain
            Current = Current.BaseType;
        }

        return false; // hit top of chain without finding concrete class
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

    void GenerateCode()
    {
        //bool HasSetupBridge = Symbol.GetMembers()
        //	.OfType<IMethodSymbol>()
        //	.Any(m => m.Name == "SetupBridge"
        //		   && m.Parameters.Length == 1
        //		   && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, CoreInterface)
        //		   && m.ReturnsVoid
        //		   // The key: Declared in this class, not abstract/interface
        //		   && SymbolEqualityComparer.Default.Equals(m.ContainingType, Symbol)
        //		   && !m.IsAbstract);


        string BaseObjectStaticCode = "";
        string BaseObjectFieldsCode = "";
        string BaseObjectMethodsCode = "";
        if (IsBase)
        {
            BaseObjectStaticCode =
$@"public struct StaticInternal
	{{
		public static delegate*<IObject, ByteReader?, void>[][] StaticMethods = new delegate*<IObject, ByteReader?, void>[8][];

		public static void RegisterMethod(int TypeIndex, int MethodIndex, delegate*<IObject, ByteReader?, void> Method)
		{{
			// Resize outer array if TypeIndex exceeds current size
			if (TypeIndex >= StaticMethods.Length)
			{{
				Array.Resize(ref StaticMethods, TypeIndex * 2);
			}}

			var Methods = StaticMethods[TypeIndex];

			// Allocate inner array if null
			if (StaticMethods[TypeIndex] == null)
			{{
				StaticMethods[TypeIndex] = Methods = new delegate*<IObject, ByteReader?, void>[4];
			}}

			// Resize inner array if MethodIndex exceeds current length
			if (MethodIndex >= Methods.Length)
			{{
				int NewLength = Methods.Length;
				while (NewLength <= MethodIndex) NewLength *= 2;

				var MethodsIntPtr = System.Runtime.CompilerServices.Unsafe.As<delegate*<IObject, ByteReader?, void>[], IntPtr[]>(ref Methods);

				Array.Resize(ref MethodsIntPtr, NewLength);

				Methods = System.Runtime.CompilerServices.Unsafe.As<IntPtr[], delegate*<IObject, ByteReader?, void>[]>(ref MethodsIntPtr);

				StaticMethods[TypeIndex] = Methods;
			}}

			Methods[MethodIndex] = Method;
		}}
	}}";



            BaseObjectFieldsCode = $@"
	{(Symbol.IsAbstract ? $@"
	public abstract IObject? Outer {{ get; }}
    public abstract uint ObjectId {{ get; }}
    public abstract int ObjectIndex {{ get; }}
    public abstract IReadOnlyList<IObject> DefaultSubobjects {{ get; }}"
: (IsBase ? @"
	IObject? PrivateOuter = null;
    public IObject? Outer { get => PrivateOuter; }

    UInt32 PrivateObjectId { get; set; } = 0;
    public UInt32 ObjectId { get => PrivateObjectId; }

	Int32 PrivateObjectIndex = -1;
	public Int32 ObjectIndex { get => PrivateObjectIndex; }

    Int32 NextFreeDefaultSubobjectIndex = 2;  
	ConcurrentStack<Int32> FreeDefaultIndices = new ConcurrentStack<Int32>();

    private readonly List<IObject> PrivateDefaultSubobjects = new List<IObject>();
    public IReadOnlyList<IObject> DefaultSubobjects => PrivateDefaultSubobjects;" : ""))}
				";
        }


        var StaticInitCode = $@"
	#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
	[System.Runtime.CompilerServices.ModuleInitializer]
	#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
	public{(IsBase ? " " : " new ")}static void StaticConstructor()
	{{
		{(Symbol.IsAbstract ? "" : $@"Astral.ObjectsManager.Register<{NameWithParams}>(Constructor: () => new {NameWithParams}(), ""{Symbol.ToDisplayString()}"", {Hash});")}
	}}";

        BaseObjectMethodsCode = $@"
	{(Symbol.IsAbstract ? $@"
	public abstract int GetTypeIndex();
	public abstract UInt64 GetTypeHash();
	
	public abstract void SetOuter(IObject NewOuter);
	public abstract void SetObjectId(UInt32 ObjectId);

    public abstract void CreatedAsSubobject(IObject Outer, Int32 ObjectIndex);
    public abstract void AddDefualtSubobject(IObject DefualtSubobject);

	public abstract void InvokeMethod(int MethodIndex, ByteReader? Reader = null);"
: (!IsBase ? $@"
	public override UInt64 GetTypeHash() => {Hash};
	public override int GetTypeIndex() => Astral.StaticObject<{NameWithParams}>.Index;"
: $@"
	public virtual UInt64 GetTypeHash() => {Hash};
	public virtual int GetTypeIndex() => Astral.StaticObject<{NameWithParams}>.Index;

	public void SetOuter(IObject NewOuter)
    {{
        PrivateOuter = NewOuter;
    }}

	public void SetObjectId(UInt32 ObjectId)
    {{
        PrivateObjectId = ObjectId;
    }}

	public void InvokeMethod(int MethodIndex, ByteReader? Reader = null)
	{{
		StaticInternal.StaticMethods[Astral.StaticObject<{NameWithParams}>.Index][MethodIndex](this, Reader);
	}}

	// Call ""Parent"" method when you override this.
    public virtual void CreatedAsSubobject(IObject Outer, Int32 ObjectIndex)
    {{
        PrivateOuter = Outer;
        PrivateObjectIndex = ObjectIndex;
    }}

    // Call ""Parent"" method when you override this.
    public virtual void AddDefualtSubobject(IObject DefualtSubobject)
    {{
        if (DefualtSubobject.ObjectIndex > -1) throw new InvalidOperationException(""Target object is already registered as subobject."");

        if (DefaultSubobjects.Count < 2)
        {{
            PrivateDefaultSubobjects.Add(DefualtSubobject);
            DefualtSubobject.CreatedAsSubobject(this, 1);
            return;
        }}

        if (FreeDefaultIndices.TryPop(out Int32 DefIndex))
        {{
            PrivateDefaultSubobjects[DefIndex] = DefualtSubobject;
            DefualtSubobject.CreatedAsSubobject(this, DefIndex);
            return;
        }}

        DefIndex = NextFreeDefaultSubobjectIndex++;

        PrivateDefaultSubobjects.Add(DefualtSubobject);
        DefualtSubobject.CreatedAsSubobject(this, DefIndex);
    }}
"))}";



        Code =
$@"#pragma warning disable CS8618
using Astral.Interfaces;
using Astral.Serialization;
using System.Collections.Concurrent;

//using {Symbol.ContainingNamespace.ToDisplayString()};

namespace {Symbol.ContainingNamespace.ToDisplayString()};

{AccessModifierStr} unsafe{(!Symbol.IsAbstract ? " " : " abstract ")}partial class {NameWithParams}
{{
	{BaseObjectStaticCode}
	{BaseObjectFieldsCode}
	{StaticInitCode}
	{(Symbol.Constructors.Where(ctor => !ctor.IsImplicitlyDeclared).Any(ctor => ctor.Parameters.Length == 0) ? "" : $@"
	protected {Symbol.Name}() {{}}")}
	{BaseObjectMethodsCode}
}}
#pragma warning restore CS8618";
    }
}