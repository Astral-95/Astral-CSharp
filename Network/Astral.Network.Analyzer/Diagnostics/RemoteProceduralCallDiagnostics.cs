using Microsoft.CodeAnalysis;

namespace Astral.Network.Analyzer.Diagnostics;

public class RemoteProceduralCallDiagnostics
{
    public static DiagnosticDescriptor UnsupportedParameterType { get; private set; } = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
    id: "NET0",
#pragma warning restore RS2008 // Enable analyzer release tracking
    title: "Unsupported RPC Parameter Type",
    messageFormat: "RemoteMethod '{0}' has unsupported parameter type '{1}' for RPC",
    category: "RPC",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

    public static DiagnosticDescriptor UnsupportedReturnType { get; private set; } = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
    id: "NET1",
#pragma warning restore RS2008 // Enable analyzer release tracking
    title: "Unsupported RPC return Type",
    messageFormat: "RemoteMethod '{0}' has unsupported return type '{1}' for RPC",
    category: "RPC",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

    public static DiagnosticDescriptor IncorrectNameSuffix { get; private set; } = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
    id: "NET2",
#pragma warning restore RS2008 // Enable analyzer release tracking
    title: "Unsupported RPC return Type",
    messageFormat: "RemoteMethod '{0}' requires a '_Receive' suffix, Expected name: '{0}_Receive'",
    category: "RPC",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

    public static DiagnosticDescriptor IncorrectNameSuffixSend { get; private set; } = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
    id: "NET3",
#pragma warning restore RS2008 // Enable analyzer release tracking
    title: "Unsupported RPC return Type",
    messageFormat: "RemoteMethod '{0}' requires a '_Receive' suffix, Expected name: '{0}_Receive'",
    category: "RPC",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

    public static DiagnosticDescriptor ReservedSuffix { get; private set; } = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
    id: "NET4",
#pragma warning restore RS2008 // Enable analyzer release tracking
    title: "Unsupported RPC return Type",
    messageFormat: "'_Send' suffix is not allowed",
    category: "RPC",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);


    public static readonly DiagnosticDescriptor Recursion = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
    id: "NET5",
#pragma warning restore RS2008 // Enable analyzer release tracking
    title: "Unsupported RPC return Type",
    messageFormat: "Infinite recursion detected",
    category: "RPC",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

    public static DiagnosticDescriptor StaticOrAbstract { get; private set; } = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
    id: "NET6",
#pragma warning restore RS2008 // Enable analyzer release tracking
    title: "Unsupported method type",
    messageFormat: "RemoteMethod '{0}' Cannot static or abstract",
    category: "RPC",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
}