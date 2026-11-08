using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FlowOnRails.AspNetCore.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class ProblemGenerator : IIncrementalGenerator
{
    private const string Prefix = "FlowOnRails.AspNetCore.Translation.";
    private static readonly DiagnosticDescriptor InvalidMethod = Rule("001", "Invalid configuration method",
        "'{0}' must be a synchronous, non-generic static void method with a body and exactly one by-value {1} parameter");
    private static readonly DiagnosticDescriptor InvalidClass = Rule("002", "Invalid configuration class",
        "'{0}' must be a top-level, non-generic static partial class (not file-local)");
    private static readonly DiagnosticDescriptor InvalidMarkers = Rule("003", "Invalid projection attributes", "{0}");
    private static readonly DiagnosticDescriptor InvalidStatus = Rule("004", "Invalid error status",
        "Status '{0}' must be from 400 through 599");
    private static readonly DiagnosticDescriptor Duplicate = Rule("005", "Duplicate configuration", "{0}");
    private static readonly DiagnosticDescriptor InvalidScope = Rule("006", "Error assembly not selected", "{0}");
    private static readonly DiagnosticDescriptor Reserved = Rule("007", "Reserved property omitted",
        "Automatic property '{0}' conflicts with a ProblemDetails member and is omitted; use [UseDataField] or manual extensions",
        DiagnosticSeverity.Warning, "Projection");
    private static readonly DiagnosticDescriptor Unsupported = Rule("008", "Unsupported automatic property",
        "Property '{0}' cannot be copied into object-valued extensions; use manual extensions", category: "Projection");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        foreach (var marker in new[] { "HttpProblem", "GenericProblem", "AutomaticExtensions", "UseDataField" })
        {
            var invalidTargets = context.SyntaxProvider.ForAttributeWithMetadataName(
                    Prefix + marker + "Attribute",
                    static (node, _) => node is not MethodDeclarationSyntax,
                    static (syntax, _) => syntax.TargetSymbol is IMethodSymbol method && method.MethodKind != MethodKind.Ordinary
                        ? Diagnostic.Create(InvalidMethod, syntax.TargetNode.GetLocation(), method.Name, "configuration builder")
                        : null)
                .Where(static diagnostic => diagnostic is not null);

            context.RegisterSourceOutput(invalidTargets, static (output, diagnostic) => output.ReportDiagnostic(diagnostic!));
        }

        var invalidContainers = context.SyntaxProvider.ForAttributeWithMetadataName(
                Prefix + "ErrorAssemblyAttribute",
                static (node, _) => node is not ClassDeclarationSyntax,
                static (syntax, _) => Diagnostic.Create(InvalidClass, syntax.TargetNode.GetLocation(), syntax.TargetSymbol.Name));
        context.RegisterSourceOutput(invalidContainers, static (output, diagnostic) => output.ReportDiagnostic(diagnostic));

        // Bound discovery to attributed configuration classes, not a whole-compilation provider.
        var profiles = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (syntax, cancellation) => BuildProfile(syntax, cancellation))
            .Where(static profile => profile is not null)
            .Select(static (profile, _) => profile!);

        context.RegisterSourceOutput(profiles, static (output, profile) =>
        {
            foreach (var diagnostic in profile.Diagnostics)
                output.ReportDiagnostic(diagnostic);
        });

        // Equatable source strings stop unchanged output from propagating through the graph.
        var sources = profiles
            .Where(static profile => profile.Sources is not null)
            .Select(static (profile, _) => profile.Sources!)
            .WithTrackingName("CatalogSources");

        context.RegisterSourceOutput(sources, static (output, source) =>
            output.AddSource(source.Hint + ".Registration.g.cs", SourceText.From(source.Entry, Encoding.UTF8)));
        context.RegisterImplementationSourceOutput(sources, static (output, source) =>
            output.AddSource(source.Hint + ".Implementation.g.cs", SourceText.From(source.Implementation, Encoding.UTF8)));
    }

    private static bool IsCandidate(SyntaxNode node) =>
        node is ClassDeclarationSyntax declaration &&
        (declaration.AttributeLists.Count > 0 ||
         declaration.Members.OfType<MethodDeclarationSyntax>().Any(method => method.AttributeLists.Count > 0));

    private static Profile? BuildProfile(GeneratorSyntaxContext syntax, CancellationToken cancellation)
    {
        var declaration = (ClassDeclarationSyntax)syntax.Node;
        var type = syntax.SemanticModel.GetDeclaredSymbol(declaration, cancellation);

        if (type is null)
            return null;

        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary &&
                             (Has(method, "HttpProblem") || Has(method, "GenericProblem") ||
                              Has(method, "AutomaticExtensions") || Has(method, "UseDataField")))
            .OrderBy(method => method.Name, StringComparer.Ordinal).ToArray();
        var assemblyMarkers = type.GetAttributes().Where(attribute => Is(attribute, "ErrorAssembly")).ToArray();

        if (methods.Length == 0 && assemblyMarkers.Length == 0)
            return null;

        var first = type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellation))
            .Where(IsCandidate)
            .OrderBy(node => node.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(node => node.SpanStart).First();

        if (first.SyntaxTree != declaration.SyntaxTree || first.SpanStart != declaration.SpanStart)
            return null;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var location = declaration.Identifier.GetLocation();
        var declarations = type.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellation));

        if (!type.IsStatic || type.IsGenericType || type.ContainingType is not null || type.IsFileLocal ||
            declarations.OfType<ClassDeclarationSyntax>().Any(part => !part.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            diagnostics.Add(Diagnostic.Create(InvalidClass, location, type.Name));

            return new(null, diagnostics.ToImmutable());
        }

        var compilation = syntax.SemanticModel.Compilation;
        var errorBase = compilation.GetTypeByMetadataName("FlowOnRails.Error");

        if (errorBase is null)
            return null;

        var assemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default) { compilation.Assembly };

        foreach (var marker in assemblyMarkers)
        {
            if (marker.ConstructorArguments.Length != 1 || marker.ConstructorArguments[0].Value is not INamedTypeSymbol anchor)
            {
                diagnostics.Add(Diagnostic.Create(InvalidScope, location, "ErrorAssembly requires a type identifying a referenced assembly."));
                continue;
            }

            assemblies.Add(anchor.ContainingAssembly);
        }

        var known = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        foreach (var candidate in Enumerate(assembly.GlobalNamespace, cancellation))
            if (!IsOpen(candidate) && DerivesFrom(candidate, errorBase) && compilation.IsSymbolAccessibleWithin(candidate, type))
                known.Add(Name(candidate));

        var maps = new List<Map>();
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var genericCount = 0;

        foreach (var method in methods)
        {
            cancellation.ThrowIfCancellationRequested();
            var methodLocation = method.Locations.FirstOrDefault(item => item.IsInSource) ?? location;
            var http = Attribute(method, "HttpProblem");
            var generic = Attribute(method, "GenericProblem");
            var automatic = Has(method, "AutomaticExtensions");
            var data = Has(method, "UseDataField");

            if ((http is null && generic is null) || (http is not null && generic is not null) ||
                (data && !automatic) || (generic is not null && (automatic || data)))
            {
                diagnostics.Add(Diagnostic.Create(InvalidMarkers, methodLocation,
                    "Use one of [HttpProblem] or [GenericProblem]; [UseDataField] requires [AutomaticExtensions] on an HTTP error map."));
                continue;
            }

            var isGeneric = generic is not null;
            var expected = isGeneric ? "GenericProblemBuilder" : "ProblemBuilder<TError>";
            var parameterType = method.Parameters.Length == 1 ? method.Parameters[0].Type as INamedTypeSymbol : null;
            var signature = method.MethodKind == MethodKind.Ordinary && method.IsStatic && !method.IsAsync &&
                            !method.IsGenericMethod && method.ReturnsVoid && method.Parameters.Length == 1 &&
                            method.Parameters[0].RefKind == RefKind.None && !method.Parameters[0].IsParams &&
                            !method.Parameters[0].IsOptional && !method.IsPartialDefinition &&
                            method.DeclaringSyntaxReferences.Any(reference =>
                                reference.GetSyntax(cancellation) is MethodDeclarationSyntax node &&
                                (node.Body is not null || node.ExpressionBody is not null)) &&
                            parameterType is not null &&
                            parameterType.ContainingNamespace.ToDisplayString() == Prefix.TrimEnd('.') &&
                            parameterType.MetadataName == (isGeneric ? "GenericProblemBuilder" : "ProblemBuilder`1");

            if (!signature)
            {
                diagnostics.Add(Diagnostic.Create(InvalidMethod, methodLocation, method.Name, expected));
                continue;
            }

            var attribute = http ?? generic!;
            var status = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int number
                ? number : 500;

            if (status < 400 || status > 599)
            {
                diagnostics.Add(Diagnostic.Create(InvalidStatus, methodLocation, status));
                continue;
            }

            if (isGeneric)
            {
                genericCount++;

                if (genericCount > 1)
                    diagnostics.Add(Diagnostic.Create(Duplicate, methodLocation, "Only one [GenericProblem] definition is allowed per catalog."));

                maps.Add(new(method.Name, null, status, false, false, ""));
                continue;
            }

            var error = parameterType!.TypeArguments[0] as INamedTypeSymbol;

            if (error is null || IsOpen(error) || !DerivesFrom(error, errorBase) || !compilation.IsSymbolAccessibleWithin(error, type))
            {
                diagnostics.Add(Diagnostic.Create(InvalidMethod, methodLocation, method.Name, "ProblemBuilder<closed accessible Error>"));
                continue;
            }

            if (!assemblies.Contains(error.ContainingAssembly))
            {
                diagnostics.Add(Diagnostic.Create(InvalidScope, methodLocation,
                    "Add [ErrorAssembly(typeof(" + error.Name + "))] to select the domain assembly for this catalog."));
                continue;
            }

            var errorName = Name(error);
            known.Add(errorName);

            if (!targets.Add(errorName))
                diagnostics.Add(Diagnostic.Create(Duplicate, methodLocation, "More than one mapping targets '" + errorName + "'."));

            var projection = automatic
                ? Project(error, type, compilation, data, diagnostics, methodLocation, cancellation)
                : "";
            maps.Add(new(method.Name, errorName, status, automatic, data, projection));
        }

        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            return new(null, diagnostics.ToImmutable());

        return new(Emit(type, maps, known), diagnostics.ToImmutable());
    }

    private static string Project(
        INamedTypeSymbol error,
        INamedTypeSymbol profile,
        Compilation compilation,
        bool data,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        Location methodLocation,
        CancellationToken cancellation)
    {
        var code = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = error; current is not null; current = current.BaseType)
        foreach (var member in current.GetMembers().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();

            // Inaccessible declarations do not hide an accessible base member at this call site.
            if (!compilation.IsSymbolAccessibleWithin(member, profile))
                continue;

            if (!seen.Add(member.Name) || member is not IPropertySymbol property ||
                property.IsStatic || property.IsIndexer || property.GetMethod is null ||
                property.DeclaredAccessibility != Accessibility.Public ||
                property.GetMethod.DeclaredAccessibility != Accessibility.Public ||
                !compilation.IsSymbolAccessibleWithin(property.GetMethod, profile))
                continue;

            if (!data && IsReserved(property.Name))
            {
                var location = property.Locations.FirstOrDefault(item => item.IsInSource) ?? methodLocation;
                diagnostics.Add(Diagnostic.Create(Reserved, location, property.Name));
                continue;
            }

            if (property.Type.IsRefLikeType || property.Type.TypeKind == TypeKind.Pointer ||
                property.Type.TypeKind == TypeKind.FunctionPointer)
            {
                diagnostics.Add(Diagnostic.Create(Unsupported, methodLocation, property.Name));
                continue;
            }

            code.Append("        extensions[nameof(").Append(Name(error)).Append(".@").Append(property.Name)
                .Append(")] = error.@").Append(property.Name).AppendLine(";");
        }

        return code.ToString();
    }

    private static Sources Emit(INamedTypeSymbol profile, List<Map> maps, SortedSet<string> known)
    {
        var ns = profile.ContainingNamespace.IsGlobalNamespace ? null : profile.ContainingNamespace.ToDisplayString();
        var access = profile.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        var header = "// <auto-generated/>\n#nullable enable\n" + (ns is null ? "" : "namespace " + ns + ";\n");
        var declaration = access + " static partial class @" + profile.Name + "\n{\n";
        var entry = new StringBuilder(header).Append(declaration)
            .AppendLine("    /// <summary>Registers this generated error translation catalog.</summary>")
            .Append("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add")
            .Append(profile.Name).AppendLine("(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)")
            .AppendLine("    {")
            .AppendLine("        global::System.ArgumentNullException.ThrowIfNull(services);")
            .AppendLine("        __FlowOnRailsRegister(services);")
            .AppendLine("        return services;")
            .AppendLine("    }")
            .AppendLine("    static partial void __FlowOnRailsRegister(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services);")
            .AppendLine("}");
        var implementation = new StringBuilder(header).Append(declaration)
            .AppendLine("    static partial void __FlowOnRailsRegister(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)")
            .AppendLine("    {")
            .AppendLine("        global::Microsoft.Extensions.DependencyInjection.TranslationServiceExtensions.AddFlowOnRailsTranslation(services, catalog =>")
            .AppendLine("        {")
            .Append("            catalog.Discover(");
        implementation.Append(string.Join(", ", known.Select(name => "typeof(" + name + ")"))).AppendLine(");");

        for (var index = 0; index < maps.Count; index++)
        {
            var map = maps[index];

            if (map.Error is null)
                implementation.Append("            catalog.Generic(").Append(map.Status).Append(", @").Append(map.Method).AppendLine(");");
            else
            {
                implementation.Append("            catalog.Add<").Append(map.Error).Append(">(").Append(map.Status)
                    .Append(", @").Append(map.Method);

                if (map.Automatic)
                    implementation.Append(", __FlowOnRailsProject").Append(index).Append(", ").Append(map.Data ? "true" : "false");

                implementation.AppendLine(");");
            }
        }

        implementation.AppendLine("        });").AppendLine("    }");

        for (var index = 0; index < maps.Count; index++)
        {
            var map = maps[index];

            if (!map.Automatic)
                continue;

            implementation.Append("    private static void __FlowOnRailsProject").Append(index).Append('(').Append(map.Error)
                .AppendLine(" error, global::System.Collections.Generic.IDictionary<string, object?> extensions)")
                .AppendLine("    {").Append(map.Projection).AppendLine("    }");
        }

        implementation.AppendLine("}");
        // Bound file names for hosts that persist generated sources, including long namespaces.
        using var hash = SHA256.Create();
        var digest = hash.ComputeHash(Encoding.UTF8.GetBytes(Name(profile)));
        var hint = profile.Name.Substring(0, Math.Min(profile.Name.Length, 32)) + "." +
            string.Concat(digest.Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

        return new(hint, entry.ToString(), implementation.ToString());
    }

    private static IEnumerable<INamedTypeSymbol> Enumerate(INamespaceOrTypeSymbol container, CancellationToken cancellation)
    {
        foreach (var member in container.GetMembers())
        {
            cancellation.ThrowIfCancellationRequested();

            if (member is INamespaceSymbol ns)
            {
                foreach (var nested in Enumerate(ns, cancellation))
                    yield return nested;
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;

                foreach (var nested in Enumerate(type, cancellation))
                    yield return nested;
            }
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol root)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, root))
                return true;

        return false;
    }

    private static bool IsOpen(INamedTypeSymbol type) =>
        type.IsUnboundGenericType || type.TypeArguments.Any(argument =>
            argument is ITypeParameterSymbol || argument is INamedTypeSymbol named && IsOpen(named)) ||
        type.ContainingType is not null && IsOpen(type.ContainingType);

    private static bool IsReserved(string name) =>
        name is "type" or "title" or "status" or "detail" or "instance";

    private static string Name(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    private static bool Has(ISymbol symbol, string name) => Attribute(symbol, name) is not null;
    private static bool Is(AttributeData attribute, string name) => attribute.AttributeClass?.ToDisplayString() == Prefix + name + "Attribute";
    private static AttributeData? Attribute(ISymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(attribute => Is(attribute, name));

    private static DiagnosticDescriptor Rule(
        string id,
        string title,
        string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        string category = "Configuration") =>
        new("FORHTTP" + id, title, message, category, severity, isEnabledByDefault: true);

    private sealed record Map(string Method, string? Error, int Status, bool Automatic, bool Data, string Projection);
    private sealed record Sources(string Hint, string Entry, string Implementation);
    private sealed record Profile(Sources? Sources, ImmutableArray<Diagnostic> Diagnostics);
}
