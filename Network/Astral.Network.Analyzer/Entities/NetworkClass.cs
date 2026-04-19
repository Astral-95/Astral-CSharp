using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Astral.Analyzer.Entities;

public class NetworkClass
{
    Compilation Compilation;
    SourceProductionContext Spc;
    public INamedTypeSymbol Symbol;
    public INamedTypeSymbol NetworkInterface;

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

    public List<NetworkMethod> Methods { get; private set; } = new List<NetworkMethod>();
    public List<string> MethodNames { get; private set; } = [];


    public string Code { get; private set; } = "";
    string MethodsStaticClassCode { get; set; } = "";
    string StaticInitCode { get; set; } = "";
    string SendMethodDefinitionsCode { get; set; } = "";

    List<string> SendMethodDefinitions { get; set; } = [];
    List<string> StaticMethodDefinitions { get; set; } = [];



    //string FieldsCode = "";

    public NetworkClass(Compilation Compilation, SourceProductionContext Spc, INamedTypeSymbol Symbol, INamedTypeSymbol NetworkInterface)
    {
        this.Compilation = Compilation;
        this.Spc = Spc;
        this.Symbol = Symbol;
        this.NetworkInterface = NetworkInterface;
        Name = Symbol.Name;
        //IsObject = Name == "Object";
        FullName = Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var BaseType = Symbol.BaseType;
        IsBase = BaseType == null || !BaseType.AllInterfaces.Contains(NetworkInterface, SymbolEqualityComparer.Default);

        NonAbstractOuterImplemented = HasNonAbstractParentWithNetworkInterface();

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

        ProcessMethods(Compilation);
        GenerateCode();
    }


    bool HasNonAbstractParentWithNetworkInterface()
    {
        var Current = Symbol.BaseType;

        while (Current != null)
        {
            // Stop if this base class does NOT implement the interface
            if (!Current.AllInterfaces.Contains(NetworkInterface, SymbolEqualityComparer.Default))
                return false;

            // If this base class is concrete (non-abstract), return true
            if (!Current.IsAbstract)
                return true;

            // Move up the chain
            Current = Current.BaseType;
        }

        return false; // hit top of chain without finding concrete class
    }

    void CollectMethodsInHierarchy()
    {
        var Current = Symbol.BaseType;

        //ClassMethodsArray.Clear();

        while (Current != null && Current.SpecialType != SpecialType.System_Object)
        {
            // fallback for metadata (external assembly) types
            var DirectMethods = Current.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => IsSuportedMethod(m)) // your detection rule
                .Select(m => $"{Current.ToDisplayString()}.StaticMethods.{m.Name}");

            MethodNames.AddRange(DirectMethods);

            Current = Current.BaseType;
        }

        // Severse so base-first order (NetworkObject → Derived)
        MethodNames.Reverse();
    }

    static bool IsSuportedMethod(IMethodSymbol MethodSymbol)
    {
        var MethodAttribute = MethodSymbol.GetAttributes()
                    .FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == "Astral.Attributes.Method" ||
                    a.AttributeClass?.ToDisplayString() == "Astral.Attributes.MethodAttribute");

        if (MethodAttribute == null) return false;

        List<string> MethodTags = new List<string>();

        if (!MethodAttribute.ConstructorArguments.IsEmpty)
        {
            TypedConstant ConstructorArg = MethodAttribute.ConstructorArguments[0];

            if (ConstructorArg.Kind == TypedConstantKind.Array)
            {
                foreach (TypedConstant typedConstant in ConstructorArg.Values)
                {
                    if (typedConstant.Value is not string Tag) continue;
                    MethodTags.Add(Tag);
                }
            }
        }

        return MethodTags.Contains("Remote");
    }

    void ProcessMethods(Compilation Compilation)
    {
        CollectMethodsInHierarchy();

        var MethodSymbols = Symbol.GetMembers().OfType<IMethodSymbol>().ToList();

        int Index = MethodNames.Count;
        foreach (var MethodSymbol in MethodSymbols)
        {
            var MethodAttribute = MethodSymbol.GetAttributes()
                .FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == "Astral.Attributes.Method" ||
                a.AttributeClass?.ToDisplayString() == "Astral.Attributes.MethodAttribute");

            if (MethodAttribute == null) continue;

            List<string> MethodTags = new List<string>();

            if (!MethodAttribute.ConstructorArguments.IsEmpty)
            {
                TypedConstant ConstructorArg = MethodAttribute.ConstructorArguments[0];

                if (ConstructorArg.Kind == TypedConstantKind.Array)
                {
                    foreach (TypedConstant typedConstant in ConstructorArg.Values)
                    {
                        if (typedConstant.Value is not string Tag) continue;
                        MethodTags.Add(Tag);
                    }
                }
            }

            if (!MethodTags.Contains("Remote")) continue;

            var Method = new NetworkMethod(Compilation, Spc, this, MethodSymbol, MethodTags.ToArray(), Index);
            if (!Method.Valid) continue;

            MethodNames.Add($"NetworkStaticMethods.{Method.Name}");
            SendMethodDefinitions.Add(Method.SendDefinition);
            StaticMethodDefinitions.Add(Method.StaticDefinition);

            Methods.Add(Method);
            Index++;
        }
    }

    bool HasMethod(INamedTypeSymbol Symbol, string MethodName, bool Includeinterfaces = false)
    {
        for (var Current = Symbol; Current != null; Current = Current.BaseType)
        {
            var Method = Current.GetMembers(MethodName)
                                .OfType<IMethodSymbol>()
                                .FirstOrDefault();
            if (Method != null)
                return true;
        }

        if (Includeinterfaces)
        {
            foreach (var iface in Symbol.AllInterfaces)
            {
                var Method = iface.GetMembers(MethodName)
                                  .OfType<IMethodSymbol>()
                                  .FirstOrDefault();
                if (Method != null)
                    return true;
            }
        }

        return false;
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


    public string CreateFieldsCode_Abstract()
    {
        string FieldsCode = string.Empty;

        return @"
    public abstract UInt32 NetworkId { get; }
    public abstract INetworkFlags NetworkFlags { get; set; }
    public abstract List<WeakReference<INetworkObject>> ReferencedObjects { get; }
    public abstract List<WeakReference<INetworkObject>> DeltaReferencedObjects { get; }

    public abstract ConcurrentQueue<(int MethodIndex, PooledNetByteWriter? Writer)> PrivateRemoteCallQueue { get; }
    public abstract ConcurrentQueue<(int MethodIndex, PooledNetByteWriter? Writer)> RemoteCallReliableQueue { get; }
";
    }

    public string CreateFieldsCode_NonAbstract()
    {
        if (!IsBase) return "";

        return @"
    UInt32 PrivateNetworkId = 0;
	public UInt32 NetworkId { get => PrivateNetworkId; }

    public NetworkObjectFlags INetworkFlags { get; set; } = NetworkObjectFlags.None;

    List<WeakReference<INetworkObject>> PrivateReferencedObjects = new();
    public List<WeakReference<INetworkObject>> ReferencedObjects { get => PrivateReferencedObjects; }

    List<WeakReference<INetworkObject>> PrivateDeltaReferencedObjects = new();
    public List<WeakReference<INetworkObject>> DeltaReferencedObjects { get => PrivateDeltaReferencedObjects; }

	ConcurrentQueue<(int MethodIndex, PooledNetByteWriter? Writer)> PrivateRemoteCallQueue = new();
	public ConcurrentQueue<(int MethodIndex, PooledNetByteWriter? Writer)> RemoteCallQueue { get => PrivateRemoteCallQueue; }

	ConcurrentQueue<(int MethodIndex, PooledNetByteWriter? Writer)> PrviateRemoteCallReliableQueue = new();
	public ConcurrentQueue<(int MethodIndex, PooledNetByteWriter? Writer)> RemoteCallReliableQueue { get => PrviateRemoteCallReliableQueue; }";
    }

    public string CreateMethodsCode_Abstract()
    {
        return @"
    public abstract void InitNetworkObject(NetaDriver Driver);

    public abstract void PreDestroy();

	public abstract void CreatedFromRemote();
	public abstract void AssignedFromRemote();
    public abstract void PreDestroyFromRemote();

	public abstract void EnqueueRemoteCall(int MethodIndex, PooledNetByteWriter? Writer);
	public abstract void EnqueueRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer);
	public abstract void EnqueueRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer);

	public abstract void EnqueueMulticastRemoteCall(int MethodIndex, PooledNetByteWriter? Writer);
	public abstract void EnqueueMulticastRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer);
	public abstract void EnqueueMulticastRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer);";
    }

    public string CreateMethodsCode_Channel()
    {
        if (!IsBase) return "";
        return @"
    public virtual void EnqueueMulticastRemoteCall(int MethodIndex, PooledNetByteWriter? Writer) => throw new InvalidOperationException();
	public virtual void EnqueueMulticastRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer) => throw new InvalidOperationException();
	public virtual void EnqueueMulticastRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer) => throw new InvalidOperationException();

	class NetaChannel_EnqueueRemoteCall { }
	public virtual void EnqueueRemoteCall(int MethodIndex, PooledNetByteWriter? Writer)
	{
		OutBunch Bunch = CreateBunch<NetaChannel_EnqueueRemoteCall>();
		EnqeueueRemoteCall(MethodIndex, Bunch, Writer);
	}

	class NetaChannel_EnqueueRemoteCall_Reliable { }
	public virtual void EnqueueRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer)
	{
		OutBunch Bunch = CreateBunch<NetaChannel_EnqueueRemoteCall_Reliable>();
		Bunch.SetIsReliable();
		EnqeueueReliableRemoteCall(MethodIndex, Bunch, Writer);
	}

	class NetaChannel_EnqueueRemoteCall_ReliableOrdered { }
	public virtual void EnqueueRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer)
	{
		OutBunch Bunch = CreateBunch<NetaChannel_EnqueueRemoteCall_ReliableOrdered>();
		Bunch.SetIsReliable();
		Bunch.SetIsOrdered();
		EnqeueueReliableRemoteCall(MethodIndex, Bunch, Writer);
	}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
	void EnqeueueRemoteCall(int MethodIndex, OutBunch Bunch, PooledNetByteWriter? Writer)
	{
		Bunch.SerializeObject(this);
		Bunch.Serialize(MethodIndex);
		if (Writer != null)
		{
            foreach (var RefObj in Writer.ReferencedObjects)
            {
                if (!Connection.PackageMap.ObjectIdMappings.ContainsKey(RefObj)) Connection.PackageMap.MapObject(RefObj);
            }
			Bunch.Serialize(Writer);
			Writer.Return();
		}

		Bunch.FinalizeBunch();
		Connection.SendBunch(Bunch);
	}
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void EnqeueueReliableRemoteCall(int MethodIndex, OutBunch Bunch, PooledNetByteWriter? Writer)
	{
		Bunch.SerializeObject(this);
		Bunch.Serialize(MethodIndex);
		if (Writer != null)
		{
            foreach (var RefObj in Writer.ReferencedObjects)
            {
                if (!Connection.PackageMap.ObjectIdMappings.ContainsKey(RefObj)) Connection.PackageMap.MapObject(RefObj);
            }
			Bunch.Serialize(Writer);
			Writer.Return();
		}

		Bunch.FinalizeBunch();
		Connection.SendReliableBunch(Bunch);
	}";
    }

    public string CreateMethodsCode_Connection()
    {
        if (!IsBase) return "";
        return @"
    public virtual void EnqueueRemoteCall(int MethodIndex, PooledNetByteWriter? Writer) => throw new NotImplementedException();
	public virtual void EnqueueRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer) => throw new NotImplementedException();
	public virtual void EnqueueRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer) => throw new NotImplementedException();
	public virtual void EnqueueMulticastRemoteCall(int MethodIndex, PooledNetByteWriter? Writer) => throw new NotImplementedException();
	public virtual void EnqueueMulticastRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer) => throw new NotImplementedException();
	public virtual void EnqueueMulticastRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer) => throw new NotImplementedException();";
    }

    public string CreateMethodsCode_NetworkObject()
    {
        if (!IsBase) return "";
        return @"
    public virtual void EnqueueRemoteCall(int MethodIndex, PooledNetByteWriter? Writer)
	{{
		RemoteCallQueue.Enqueue((MethodIndex, Writer)); MarkDirty();
	}}
	public virtual void EnqueueRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer)
	{{
		RemoteCallReliableQueue.Enqueue((MethodIndex, Writer)); MarkDirty();
	}}
	public virtual void EnqueueRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer)
	{{
		RemoteCallReliableQueue.Enqueue((MethodIndex, Writer)); MarkDirty();
	}}

	public virtual void EnqueueMulticastRemoteCall(int MethodIndex, PooledNetByteWriter? Writer)
	{{
		RemoteCallQueue.Enqueue((MethodIndex, Writer)); MarkDirty();
	}}
	public virtual void EnqueueMulticastRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer)
	{{
		RemoteCallReliableQueue.Enqueue((MethodIndex, Writer)); MarkDirty();
	}}
	public virtual void EnqueueMulticastRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer)
	{{
		RemoteCallReliableQueue.Enqueue((MethodIndex, Writer)); MarkDirty();
	}}";
    }

    void GenerateCode()
    {
        var NetaChannelSymbol = Compilation.GetTypeByMetadataName("Astral.Network.Channels.NetaChannel");
        var NetaConnectionSymbol = Compilation.GetTypeByMetadataName("Astral.Network.Connections.NetaConnection");

        bool IsNetaChannel = IsOrSubclassOf(Symbol, NetaChannelSymbol);
        bool IsNetaConnection = IsOrSubclassOf(Symbol, NetaConnectionSymbol);

        string StaticCtorMethodsCode = "";

        int TotalMethodsCount = Methods.Count() + MethodNames.Count;
        if (TotalMethodsCount > 0)
        {
            StaticCtorMethodsCode += $"StaticInternal.RegisterMethod(Astral.StaticObject<{NameWithParams}>.Index, {0}, &{MethodNames[0]});";

            for (int i = 1; i < MethodNames.Count; i++)
            {
                StaticCtorMethodsCode += $"\n\t\tStaticInternal.RegisterMethod(Astral.StaticObject<{NameWithParams}>.Index, {i}, &{MethodNames[i]});";
            }
        }

        if (StaticMethodDefinitions.Count > 0)
        {
            MethodsStaticClassCode =
$@"public static{(IsBase ? " " : " new ")}class NetworkStaticMethods
	{{
		{string.Join("\n\t\t\t", StaticMethodDefinitions)}
	}}";
        }  

        if (StaticCtorMethodsCode != "")
        {
            StaticInitCode = $@"
	#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
	[System.Runtime.CompilerServices.ModuleInitializer]
	#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
	public{(IsBase ? " " : " new ")}static void NetworkStaticConstructor()
	{{
		{(string.IsNullOrWhiteSpace(StaticCtorMethodsCode) ? "" : StaticCtorMethodsCode)}
	}}";
        }
        
        string FieldsCode = "";
        string MethodsCode = "";
        if (Symbol.IsAbstract)
        {
            FieldsCode = CreateFieldsCode_Abstract();
            MethodsCode = CreateMethodsCode_Abstract();
        }
        else
        {
            FieldsCode = CreateFieldsCode_NonAbstract();

            if (IsBase)
            {
                MethodsCode = $@"
    public PooledNetByteWriter RentWriter() => PooledNetByteWriter.Rent();
    public void ISetNetworkId(UInt32 NewId) => PrivateNetworkId = NewId;
    
    public void InitNetworkObject(NetaDriver Driver) {{}}
    
    public virtual void PreDestroy() {{}}

    public virtual void CreatedFromRemote() {{}}
	public virtual void AssignedFromRemote() {{}}
    public virtual void PreDestroyFromRemote() {{}}
";
            }
            
            if (IsNetaChannel)
            {
                MethodsCode += "\n\t" + CreateMethodsCode_Channel();
            }
            else if (IsNetaConnection)
            {
                MethodsCode += "\n\t" + CreateMethodsCode_Connection();
            }
            else
            {
                MethodsCode += "\n\t" + CreateMethodsCode_NetworkObject();
            }
        }

   
        //------------------------------------------------------------------------
        SendMethodDefinitionsCode = string.Join("\n\t\t", SendMethodDefinitions);
        //------------------------------------------------------------------------

        if (string.IsNullOrWhiteSpace(MethodsStaticClassCode) && string.IsNullOrWhiteSpace(FieldsCode) && string.IsNullOrWhiteSpace(MethodsCode))
        {
            return;
        }

        Code =
$@"#pragma warning disable CS8618
using Astral.Network.Drivers;
using Astral.Network.Transport;
using Astral.Network.Interfaces;
using Astral.Network.Serialization;
using System.Runtime.CompilerServices;

using System.Collections.Concurrent;

namespace {Symbol.ContainingNamespace.ToDisplayString()};

{AccessModifierStr} unsafe{(!Symbol.IsAbstract ? " " : " abstract ")}partial class {NameWithParams}
{{
	{MethodsStaticClassCode}
	{FieldsCode}
	{StaticInitCode}
	{MethodsCode}
	{SendMethodDefinitionsCode}
}}
#pragma warning restore CS8618";
    }
}
