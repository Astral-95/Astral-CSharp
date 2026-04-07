using Astral.Network.Analyzer;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Astral.Analyzer.Entities;

public struct MethodParamPass
{
    public string WriterPass;
    public string ReaderPass;
    public string NamePass;
    public string ArgPass;
    public string TypeNamePass;
}

public class NetworkMethod
{
    readonly SourceProductionContext Spc;
    public NetworkClass Owner { get; set; }
    public IMethodSymbol Symbol { get; set; }
    public string[] Tags { get; set; }

    public string Name { get; set; }
    public string ContainingTypeStr { get; set; }

    public string ArgPass { get; set; }

    public string NamePass { get; set; }
    public string WriterPass { get; set; }
    public string ReaderPass { get; set; }
    public string TypeNamePass { get; set; }

    public string SendDefinition { get; set; }
    public string StaticDefinition { get; set; }
    public string MethodToCall { get; set; }

    public int Index { get; set; }


    public bool Valid = true;

    public NetworkMethod(Compilation Compilation, SourceProductionContext Spc, NetworkClass Owner, IMethodSymbol Symbol, string[] Tags, int Index)
    {
        this.Spc = Spc;
        this.Owner = Owner;
        this.Symbol = Symbol;
        this.Tags = Tags;

        Name = Symbol.Name;
        ContainingTypeStr = Symbol.ContainingType.ToDisplayString();
        this.Index = Index;

        if (!Tags.Contains("Remote")) return;

        Valid = NetworkMethodAnalyzer.Analyze(this, Compilation, Spc);
        if (!Valid) return;

        ProcessRemoteMethod();
    }



    void ProcessRemoteMethod()
    {
        GetMethodToCall();
        GenerateParameterPass();

        StaticDefinition = $@"
		public static void {Name}(Astral.Interfaces.IObject Instance, Astral.Serialization.ByteReader? Reader)
		{{
			(({Owner.NameWithParams})Instance).{Name}({ReaderPass});
		}}";

        var MethodTrimmedName = Name.EndsWith("_Receive")
                        ? Name.Substring(0, Name.Length - "_Receive".Length)
                        : Name;

        SendDefinition = $@"
	public void {MethodTrimmedName}_Send({TypeNamePass})
	{{
		{(string.IsNullOrWhiteSpace(WriterPass) ? $"{MethodToCall}({Index}, null);" : $@"
		var Writer = RentWriter();
		{WriterPass}
		{MethodToCall}({Index}, Writer);")}
	}}";
    }

    private void GenerateParameterPass()
    {
        try
        {
            if (Symbol.Parameters.Length == 0)
            {
                return;
            }

            var sbName = new System.Text.StringBuilder();
            var sbArg = new System.Text.StringBuilder();

            var sbWriter = new System.Text.StringBuilder();

            var ReaderStrs = new List<string>();
            var sbFull = new System.Text.StringBuilder();

            for (int i = 0; i < Symbol.Parameters.Length; i++)
            {
                var p = Symbol.Parameters[i];

                var StrWriter = GenerateParamWriterPass(p);
                if (StrWriter != null) sbWriter.Append(StrWriter);

                GenerateParamReaderPass(p);
                if (ReaderPass != null) ReaderStrs.Add(ReaderPass);

                if (i > 0)
                {
                    sbName.Append(", ");
                    sbArg.Append(", ");
                    sbFull.Append(", ");
                }

                sbName.Append(p.Name);
                sbArg.Append($"({p.Type.ToDisplayString()})Args[{i}]");
                sbFull.Append($"{p.Type.ToDisplayString()} {p.Name}");
            }

            if (ReaderStrs.Count > 0) ReaderPass = string.Join(", ", ReaderStrs);

            ArgPass = sbArg.ToString();
            NamePass = sbName.ToString();
            WriterPass = sbWriter.ToString();
            TypeNamePass = sbFull.ToString();
        }
        catch (Exception Ex)
        {
            throw new Exception("GenerateParameterPass", Ex);
        }
    }

    void GenerateParamReaderPass(IParameterSymbol Param)
    {
        var Type = Param.Type;
        var TypeName = Type.ToString();

        // ----- String -----
        if (TypeName == "string")
        {
            ReaderPass = "Reader!.SerializeString()";
        }
        // ----- Network Object -----
        else if (TypeName == "Astral.Network.NetworkObject" || TypeName.EndsWith("NetworkObject"))
        {
            ReaderPass = "Reader!.SerializeNetworkObject()";
        }
        // ----- Array -----
        else if (Type is IArrayTypeSymbol ArrayType)
        {
            var ElemType = ArrayType.ElementType.ToString();

            if (ElemType == "string")
                ReaderPass = "Reader!.SerializeStringArray()";
            else if (ElemType == "Astral.Network.NetworkObject" || ElemType.EndsWith("NetworkObject"))
                ReaderPass = "Reader!.SerializeNetworkObjectArray()";
            else if (ArrayType.ElementType.IsValueType || ArrayType.ElementType.SpecialType != SpecialType.None)
                ReaderPass = $"Reader!.SerializeArray<{ElemType}>()";
            else
                return; // skip unsupported managed array element
        }
        // ----- List -----
        else if (TypeName.StartsWith("System.Collections.Generic.List<"))
        {
            var ElemType = ((INamedTypeSymbol)Type).TypeArguments[0];

            if (ElemType.ToString() == "string")
                ReaderPass = "Reader!.SerializeStringList()";
            else if (ElemType.ToString() == "Astral.Network.NetworkObject" || ElemType.ToString().EndsWith("NetworkObject"))
                ReaderPass = "Reader!.SerializeNetworkObjectList()";
            else if (ElemType.IsValueType || ElemType.SpecialType != SpecialType.None)
                ReaderPass = $"Reader!.SerializeList<{ElemType}>()";
            else
                return; // skip unsupported managed list element
        }
        // ----- Primitive or Struct -----
        else if (Type.IsValueType || Type.SpecialType != SpecialType.None)
        {
            ReaderPass = $"Reader!.Serialize<{TypeName}>()";
        }
        // ----- Unsupported managed type -----
        else
        {
            ReaderPass = null;
        }
    }

    string GenerateParamWriterPass(IParameterSymbol Param)
    {
        return $"Writer!.Serialize({Param.Name});";
    }


    void GetMethodToCall()
    {
        bool IsReliable = Tags.Contains("Reliable");
        bool IsOrdered = Tags.Contains("Ordered");
        bool IsMulticast = Tags.Contains("Multicast");
        if (IsReliable && IsOrdered)
        {
            if (!IsMulticast)
            {
                MethodToCall = "EnqueueRemoteCall_ReliableOrdered";
            }
            else
            {
                MethodToCall = "EnqueueMulticastRemoteCall_ReliableOrdered";
            }
        }
        else if (IsReliable)
        {
            if (!IsMulticast)
            {
                MethodToCall = "EnqueueRemoteCall_Reliable";
            }
            else
            {
                MethodToCall = "EnqueueMulticastRemoteCall_Reliable";
            }

        }
        else
        {
            if (!IsMulticast)
            {
                MethodToCall = "EnqueueRemoteCall";
            }
            else
            {
                MethodToCall = "EnqueueMulticastRemoteCall";
            }
        }
    }
}