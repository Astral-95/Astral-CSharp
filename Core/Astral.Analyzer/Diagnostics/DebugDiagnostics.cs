using Microsoft.CodeAnalysis;

namespace Astral.Analyzer.Diagnostics;

public class DebugDiagnostics
{
    public static DiagnosticDescriptor Info { get; private set; } = new
    (
        id: "PAD",
        title: "Debug",
        messageFormat: "Parser debug: {0}",
        category: "Debug",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    public static DiagnosticDescriptor Warning { get; private set; } = new
    (
        id: "PAD",
        title: "Debug",
        messageFormat: "Parser debug: {0}",
        category: "Debug",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );
}