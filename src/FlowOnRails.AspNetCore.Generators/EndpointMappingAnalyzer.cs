using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

using FlowOnRails.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.AspNetCore.Generators;

/// <summary>Checks statically configured HTTP visibility against exact endpoint error cases.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EndpointMappingAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor MissingMapping = new(
        "FORHTTP009", "Visible endpoint error has no translation",
        "Endpoint error '{0}' requires a specific HTTP translation but no authorized mapping is configured. Register a mapping or select a generic visibility rule.",
        "Mapping", DiagnosticSeverity.Warning, true, customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    private static readonly DiagnosticDescriptor Unavailable = new(
        "FORHTTP010", "Endpoint translation analysis is unavailable",
        "HTTP translation coverage could not be fully checked: {0}. Use statically resolvable configuration and supported endpoint flow to enable the remaining checks.",
        "Mapping", DiagnosticSeverity.Info, true, customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [MissingMapping, Unavailable];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(compilationContext =>
        {
            if (compilationContext.Compilation.GetTypeByMetadataName("FlowOnRails.AspNetCore.Translation.RailResultExtensions") is null
                || !ErrorFlowAnalysis.TryCreate(compilationContext.Compilation, out var analysis))
                return;

            new Inspection(compilationContext, analysis).Run();
        });
    }

    private sealed class Inspection(CompilationAnalysisContext context, ErrorFlowAnalysis analysis)
    {
        private readonly Compilation _compilation = context.Compilation;
        private readonly CancellationToken _cancellation = context.CancellationToken;
        private readonly Dictionary<ISymbol, Application> _applications = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<RouteKey, Scope> _routeScopes = new();
        private readonly Dictionary<ISymbol, ExpressionSyntax> _initializers = new(SymbolEqualityComparer.Default);
        private readonly List<(InvocationExpressionSyntax Syntax, SemanticModel Model)> _calls = [];
        private readonly HashSet<(SyntaxTree Tree, int Position, string Reason)> _reported = [];
        private readonly HashSet<INamedTypeSymbol> _sourceTypes = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<IMethodSymbol, ImmutableArray<ErrorFlowExpressionResult>> _bodyResults = new(SymbolEqualityComparer.Default);

        internal void Run()
        {
            CollectSourceDeclarations();

            ReadApplicationRegistrations();
            ReadEndpointVisibility();
            ReadUnknownMutations();

            InspectMinimalEndpoints();
            InspectControllerActions();
        }

        private void CollectSourceDeclarations()
        {
            foreach (var tree in _compilation.SyntaxTrees)
            {
                _cancellation.ThrowIfCancellationRequested();
                if (tree.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var model = _compilation.GetSemanticModel(tree);
                var root = tree.GetRoot(_cancellation);

                foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    if (model.GetDeclaredSymbol(declaration, _cancellation) is INamedTypeSymbol type)
                        _sourceTypes.Add(type);

                foreach (var variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                    if (variable.Initializer is { } initializer && model.GetDeclaredSymbol(variable, _cancellation) is { } symbol)
                        _initializers[symbol] = initializer.Value;

                foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    _calls.Add((call, model));
            }
        }

        private void ReadApplicationRegistrations()
        {
            foreach (var (call, model) in _calls)
            {
                var method = GetInvokedMethod(call, model);
                if (IsLibrary(method, "ErrorPolicyServiceExtensions", "AddFlowOnRailsVisibility"))
                    ReadPolicy(call, model);
                else if (IsLibrary(method, "TranslationServiceExtensions", "AddFlowOnRailsTranslation"))
                    ReadTranslations(call, model);
                else if (IsGeneratedCatalog(method))
                    ReadCatalog(call, model, method!);
            }
        }

        private void ReadEndpointVisibility()
        {
            foreach (var (call, model) in _calls)
                if (IsLibrary(GetInvokedMethod(call, model), "ErrorPolicyEndpointExtensions", "WithErrorVisibility"))
                    ReadRouteScope(call, model);
        }

        private void InspectMinimalEndpoints()
        {
            foreach (var (call, model) in _calls)
                if (IsMap(GetInvokedMethod(call, model)))
                    ReadEndpoint(call, model);
        }

        private void InspectControllerActions()
        {
            foreach (var (call, model) in _calls)
            {
                var method = GetInvokedMethod(call, model);
                if (!IsConverter(method))
                {
                    if (FunctionOwner(call) is MethodDeclarationSyntax forwarding
                        && model.GetDeclaredSymbol(forwarding, _cancellation) is IMethodSymbol forwardingAction
                        && IsControllerAction(forwardingAction)
                        && ContainsConverter(method, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default), 0))
                        Report(call, "the controller HTTP conversion is hidden in a helper returning an HTTP result");
                    continue;
                }

                var declaration = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (declaration is null
                    || !SameNode(FunctionOwner(call), declaration)
                    || model.GetDeclaredSymbol(declaration, _cancellation) is not IMethodSymbol action
                    || !IsControllerAction(action))
                    continue;

                var controllerRoutes = _calls
                    .Where(item => IsControllerMap(GetInvokedMethod(item.Syntax, item.Model)))
                    .ToArray();
                if (controllerRoutes.Length == 0)
                    continue;

                if (controllerRoutes.Length > 1)
                {
                    Report(call, "multiple controller route registrations may attach different endpoint visibility scopes");
                    continue;
                }

                var controllerRoute = controllerRoutes[0];
                var receiver = InvocationReceiver(controllerRoute.Syntax, controllerRoute.Model);
                var application = ApplicationFor(receiver, controllerRoute.Model);
                if (application is null)
                {
                    Report(call, "the controller cannot be associated with its route registration's application");
                    continue;
                }

                if (action.ContainingType.IsAbstract || action.ContainingType.TypeParameters.Length != 0
                    || _sourceTypes.Any(type => !SameSymbol(type, action.ContainingType)
                        && Ancestors(type).Any(ancestor => SameSymbol(ancestor, action.ContainingType))))
                {
                    Report(call, "inherited controller actions require the concrete controller endpoint context");
                    continue;
                }

                var routeScopes = RouteScopes(controllerRoute.Syntax, controllerRoute.Model);
                CheckConverter(call, model, application, routeScopes, action.ContainingType, declaration);
            }
        }

        private void ReadPolicy(InvocationExpressionSyntax call, SemanticModel model)
        {
            var app = ApplicationFor(InvocationReceiver(call, model), model);
            if (app is null)
            {
                Report(call, "the service collection cannot be associated with an application");
                return;
            }

            if (app.HasPolicy)
                app.Global.Unknown = true;

            app.HasPolicy = true;
            ParseCallback(LastArgument(call), model, app.Global, app, true);

            if (IsConditional(call) || IsDeferred(call))
                app.Global.Unknown = true;
        }

        private void ReadTranslations(InvocationExpressionSyntax call, SemanticModel model)
        {
            var app = ApplicationFor(InvocationReceiver(call, model), model);
            if (app is null)
            {
                Report(call, "the translation registration cannot be associated with an application");
                return;
            }

            if (app.HasCatalog)
                app.CatalogUnknown = true;

            app.HasCatalog = true;

            if (IsConditional(call) || IsDeferred(call))
                app.CatalogUnknown = true;

            var lambda = Unwrap(LastArgument(call)) as LambdaExpressionSyntax;
            var parameter = LambdaParameter(lambda, model);
            if (lambda is null || parameter is null)
            {
                app.CatalogUnknown = true;
                Report(call, "the translation callback is not an inline lambda");
                return;
            }

            foreach (var expression in Statements(lambda))
            {
                if (expression is not InvocationExpressionSyntax invocation
                    || GetInvokedMethod(invocation, model) is not { } method
                    || !IsType(method.ContainingType, "FlowOnRails.AspNetCore.Translation.TranslationBuilder")
                    || !SameSymbol(GetReferencedSymbol(Receiver(invocation), model), parameter))
                {
                    app.CatalogUnknown = true;
                    continue;
                }

                if (method.Name == "Add" && method.TypeArguments.Length == 1
                    && invocation.ArgumentList.Arguments.Count >= 2
                    && model.GetConstantValue(
                        invocation.ArgumentList.Arguments[0].Expression,
                        _cancellation) is { HasValue: true, Value: int status }
                    && status >= 400 && status <= 599)
                {
                    if (app.Mappings.Any(mapping => SameType(mapping, method.TypeArguments[0])))
                        app.CatalogUnknown = true;
                    else
                        app.Mappings.Add(method.TypeArguments[0]);
                }
                else if (method.Name is not ("Generic" or "Discover"))
                    app.CatalogUnknown = true;
            }

            if (app.CatalogUnknown)
                Report(call, "the registered translation catalog contains unsupported or conflicting configuration");
        }

        private void ReadCatalog(InvocationExpressionSyntax call, SemanticModel model, IMethodSymbol registration)
        {
            var app = ApplicationFor(InvocationReceiver(call, model), model);
            if (app is null)
            {
                Report(call, "the translation registration cannot be associated with an application");
                return;
            }

            if (app.HasCatalog)
                app.CatalogUnknown = true;

            app.HasCatalog = true;

            if (IsConditional(call) || IsDeferred(call))
                app.CatalogUnknown = true;
            foreach (var method in registration.ContainingType.GetMembers().OfType<IMethodSymbol>())
                if (method.GetAttributes().Any(attribute => IsType(attribute.AttributeClass, "FlowOnRails.AspNetCore.Translation.HttpProblemAttribute"))
                    && method.Parameters.Length == 1
                    && method.Parameters[0].Type is INamedTypeSymbol { TypeArguments.Length: 1 } builder
                    && IsType(builder.OriginalDefinition, "FlowOnRails.AspNetCore.Translation.ProblemBuilder<TError>"))
                    app.Mappings.Add(builder.TypeArguments[0]);
        }

        private void ReadRouteScope(InvocationExpressionSyntax call, SemanticModel model)
        {
            var receiver = InvocationReceiver(call, model);
            var owner = RouteOwner(receiver, model);
            if (owner is null)
            {
                Report(call, "the endpoint or route-group owner cannot be resolved");
                return;
            }

            if (!_routeScopes.TryGetValue(owner, out var scope))
                _routeScopes.Add(owner, scope = new Scope());
            else
                scope.Unknown = true;

            if (HasUnknownConvention(receiver, model))
                scope.Unknown = true;

            ParseCallback(LastArgument(call), model, scope, null, false);

            if (IsConditional(call) || IsDeferred(call))
                scope.Unknown = true;
        }

        private void ParseCallback(ExpressionSyntax? expression, SemanticModel model, Scope scope, Application? app, bool policy,
            Selection? bounds = null)
        {
            var lambda = Unwrap(expression) as LambdaExpressionSyntax;
            var parameter = LambdaParameter(lambda, model);
            if (lambda is null || parameter is null)
            {
                scope.Unknown = true;
                if (expression is not null)
                    Report(expression, "the visibility callback is not an inline lambda");
                return;
            }

            foreach (var statement in Statements(lambda))
            {
                _cancellation.ThrowIfCancellationRequested();
                if (statement is not InvocationExpressionSyntax call || GetInvokedMethod(call, model) is not { } method)
                {
                    if (policy && app is not null && MarkUnknownControllers(statement, parameter, model, app))
                        continue;
                    scope.Unknown = true;
                    continue;
                }

                if (policy && method.Name == "ForController"
                    && IsType(method.ContainingType, "FlowOnRails.AspNetCore.Visibility.PolicyBuilder")
                    && SameSymbol(GetReferencedSymbol(Receiver(call), model), parameter)
                    && method.TypeArguments.Length == 1)
                {
                    var controller = method.TypeArguments[0];
                    if (app is null)
                    {
                        scope.Unknown = true;
                        continue;
                    }
                    if (!app.Controllers.TryGetValue(controller, out var local))
                        app.Controllers.Add(controller, local = new Scope());
                    ParseCallback(LastArgument(call), model, local, app, false);
                    continue;
                }

                if (method.Name is "ForTree" or "ForDescendantsOf" && method.Parameters.Length == 1
                    && IsScopeType(method.ContainingType) && ScopeReceiver(Receiver(call), parameter, policy, model)
                    && method.TypeArguments.Length == 1)
                {
                    var range = method.Name == "ForTree" ? SelectionRange.Tree : SelectionRange.Descendants;
                    var selection = new Selection(method.TypeArguments[0], range);
                    if (!Within(selection, bounds))
                        scope.Unknown = true;
                    ParseCallback(LastArgument(call), model, scope, app, false, selection);
                    continue;
                }

                if (method.Name is "TranslateEachType" or "UseGenericProblem" or "UseNearestAncestorMapping"
                    && (IsType(method.ContainingType, "FlowOnRails.AspNetCore.Visibility.RuleSelection")
                        || IsType(method.ContainingType, "FlowOnRails.AspNetCore.Visibility.RuleTree")))
                {
                    var selection = ReadSelection(Receiver(call), parameter, policy, model, bounds);

                    if (selection is null || !Within(selection, bounds))
                    {
                        scope.Unknown = true;
                        continue;
                    }

                    var behavior = method.Name switch
                    {
                        "TranslateEachType" => VisibilityBehavior.EachType,
                        "UseNearestAncestorMapping" => VisibilityBehavior.NearestAncestor,
                        _ => VisibilityBehavior.Generic
                    };
                    scope.Rules.Add(new Rule(selection, behavior));
                    continue;
                }

                scope.Unknown = true;
            }

            foreach (var first in scope.Rules)
                foreach (var second in scope.Rules)
                    if (RulesConflict(first, second))
                        scope.Unknown = true;

            if (scope.Unknown)
                Report(lambda, "the visibility scope contains unsupported, escaped, or conflicting configuration");
        }

        private Selection? ReadSelection(ExpressionSyntax? receiver, ISymbol parameter, bool policy,
            SemanticModel model, Selection? bounds)
        {
            receiver = Unwrap(receiver);
            if (bounds is not null && SameSymbol(GetReferencedSymbol(receiver, model), parameter))
                return bounds;

            if (receiver is not InvocationExpressionSyntax call
                || GetInvokedMethod(call, model) is not { TypeArguments.Length: 1, Parameters.Length: 0 } selector
                || !IsScopeType(selector.ContainingType)
                || !ScopeReceiver(Receiver(call), parameter, policy, model))
                return null;

            var range = selector.Name switch
            {
                "For" => SelectionRange.Exact,
                "ForTree" => SelectionRange.Tree,
                "ForDescendantsOf" => SelectionRange.Descendants,
                _ => (SelectionRange?)null
            };
            return range is { } selectedRange ? new Selection(selector.TypeArguments[0], selectedRange) : null;
        }

        private bool RulesConflict(Rule first, Rule second)
        {
            var sameSelection = SameType(first.Selection.Root, second.Selection.Root)
                && first.Selection.Range == second.Selection.Range;
            if (sameSelection && first.Behavior != second.Behavior)
                return true;

            return first.Behavior == VisibilityBehavior.Generic
                && first.Selection.Range != SelectionRange.Exact
                && second.Behavior != VisibilityBehavior.Generic
                && Includes(first.Selection, second.Selection.Root);
        }

        private bool MarkUnknownControllers(SyntaxNode statement, ISymbol parameter, SemanticModel model, Application app)
        {
            var controllerCalls = new List<InvocationExpressionSyntax>();

            foreach (var call in statement.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var method = GetInvokedMethod(call, model);

                if (method is { Name: "ForController", TypeArguments.Length: 1 }
                    && IsType(method.ContainingType, "FlowOnRails.AspNetCore.Visibility.PolicyBuilder")
                    && SameSymbol(GetReferencedSymbol(Receiver(call), model), parameter))
                    controllerCalls.Add(call);
            }

            if (controllerCalls.Count == 0)
                return false;

            foreach (var identifier in statement.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (!SameSymbol(model.GetSymbolInfo(identifier).Symbol, parameter))
                    continue;

                if (!controllerCalls.Any(call => call.Span.Contains(identifier.Span)))
                    return false;
            }

            foreach (var call in controllerCalls)
            {
                var controller = GetInvokedMethod(call, model)!.TypeArguments[0];

                if (!app.Controllers.TryGetValue(controller, out var scope))
                    app.Controllers.Add(controller, scope = new Scope());

                scope.Unknown = true;
            }

            Report(statement, "conditional or indirect controller visibility cannot be resolved statically");
            return true;
        }

        private void ReadEndpoint(InvocationExpressionSyntax map, SemanticModel model)
        {
            var app = ApplicationFor(InvocationReceiver(map, model), model);
            if (app is null)
            {
                Report(map, "the route handler cannot be associated with an application");
                return;
            }

            var scopes = RouteScopes(map, model);
            var handler = Unwrap(LastArgument(map));
            SyntaxNode? body = handler as AnonymousFunctionExpressionSyntax;
            SemanticModel bodyModel = model;
            IMethodSymbol? bodyMethod = null;
            if (body is null && handler is not null && HandlerMethod(handler, model) is IMethodSymbol method)
            {
                bodyMethod = method;
                var implementation = method.PartialImplementationPart ?? method;
                body = implementation.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(_cancellation);
                if (body is not null)
                    bodyModel = _compilation.GetSemanticModel(body.SyntaxTree);
            }

            if (body is null)
            {
                Report(map, "the route handler body is unavailable");
                return;
            }

            var calls = body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Where(call => SameNode(FunctionOwner(call), body)).ToArray();
            foreach (var converter in calls.Where(call => IsConverter(GetInvokedMethod(call, bodyModel))))
                CheckConverter(converter, bodyModel, app, scopes, null, body, bodyMethod);
            if (!calls.Any(call => IsConverter(GetInvokedMethod(call, bodyModel)))
                && calls.Any(call => ContainsConverter(GetInvokedMethod(call, bodyModel), new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default), 0)))
                Report(map, "the HTTP conversion is hidden in a helper returning an HTTP result");
        }

        private bool ContainsConverter(IMethodSymbol? method, HashSet<IMethodSymbol> visited, int depth)
        {
            if (method is null || depth > 4 || !visited.Add(method))
                return false;

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var body = reference.GetSyntax(_cancellation);
                var model = _compilation.GetSemanticModel(body.SyntaxTree);
                foreach (var call in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    if (SameNode(FunctionOwner(call), body)
                        && (IsConverter(GetInvokedMethod(call, model)) || ContainsConverter(GetInvokedMethod(call, model), visited, depth + 1)))
                        return true;
            }
            return false;
        }

        private void CheckConverter(InvocationExpressionSyntax call, SemanticModel model, Application app,
            List<Scope> endpointScopes, ITypeSymbol? controller, SyntaxNode body, IMethodSymbol? bodyMethod = null)
        {
            if (!IsReturned(call, body))
            {
                Report(call, "the HTTP converter cannot be associated with an endpoint return");
                return;
            }

            var point = ConverterInput(call, model, body, bodyMethod);
            if (point?.Status == ErrorFlowExpressionStatus.Unreachable)
                return;

            if (point?.Summary is not { } summary)
            {
                Report(call, "the converter input has no supported error-flow summary");
                return;
            }

            var scopes = new List<Scope>(endpointScopes);
            if (controller is not null && app.Controllers.TryGetValue(controller, out var controllerScope))
                scopes.Add(controllerScope);
            scopes.Add(app.Global);

            if (scopes.Any(scope => scope.Unknown) || app.CatalogUnknown)
            {
                Report(call, "the endpoint depends on an unresolved visibility scope or translation catalog");
                return;
            }

            foreach (var error in summary.ErrorCases)
            {
                if (!error.IsExact)
                    continue;

                var rule = FindRule(error.Type, scopes);
                if (rule is null || rule.Behavior == VisibilityBehavior.Generic)
                    continue;

                var mapped = rule.Behavior == VisibilityBehavior.EachType
                    ? app.Mappings.Any(mapping => SameType(mapping, error.Type))
                    : HasAncestorMapping(error.Type, rule.Selection, scopes, app);
                if (!mapped && _reported.Add((call.SyntaxTree, call.SpanStart, "missing:" + error.Type.ToDisplayString())))
                    context.ReportDiagnostic(Diagnostic.Create(MissingMapping, call.GetLocation(), error.Type.ToDisplayString()));
            }

            if (summary.HasUnknownErrors || summary.HasUnresolvedConditions)
                Report(call, "the converter input has additional or conditional error cases that cannot be fully checked");
        }

        private ErrorFlowExpressionResult? ConverterInput(InvocationExpressionSyntax call, SemanticModel model,
            SyntaxNode body, IMethodSymbol? method)
        {
            method ??= body is AnonymousFunctionExpressionSyntax anonymous
                ? (model.GetOperation(anonymous, _cancellation) as IAnonymousFunctionOperation)?.Symbol
                : model.GetDeclaredSymbol(body, _cancellation) as IMethodSymbol;
            if (method is null)
                return null;

            if (!_bodyResults.TryGetValue(method, out var results))
            {
                var inputs = body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                    .Where(candidate => SameNode(FunctionOwner(candidate), body) && IsConverter(GetInvokedMethod(candidate, model)))
                    .Select(candidate => ConverterSource(candidate, model))
                    .OfType<ExpressionSyntax>().ToArray();
                results = analysis.AnalyzeExpressions(method, inputs, _cancellation);
                _bodyResults.Add(method, results);
            }

            var source = ConverterSource(call, model);
            return results.FirstOrDefault(result => SameNode(result.Expression, source));
        }

        private static ExpressionSyntax? ConverterSource(InvocationExpressionSyntax call, SemanticModel model) =>
            InvocationReceiver(call, model);

        private bool HasAncestorMapping(ITypeSymbol error, Selection selected, List<Scope> scopes, Application app)
        {
            for (var ancestor = (error as INamedTypeSymbol)?.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
            {
                var rule = FindRule(ancestor, scopes);
                if (rule?.Behavior == VisibilityBehavior.Generic)
                    return false;

                if (Includes(selected, ancestor))
                    continue;

                if (rule?.Behavior == VisibilityBehavior.EachType && app.Mappings.Any(mapping => SameType(mapping, ancestor)))
                    return true;
            }
            return false;
        }

        private Rule? FindRule(ITypeSymbol error, List<Scope> scopes)
        {
            foreach (var scope in scopes)
                for (var ancestor = error as INamedTypeSymbol; ancestor is not null; ancestor = ancestor.BaseType)
                {
                    var rule = scope.Rules.Where(rule => SameType(rule.Selection.Root, ancestor) && Includes(rule.Selection, error))
                        .OrderBy(rule => rule.Selection.Range).FirstOrDefault();
                    if (rule is not null)
                        return rule;
                }
            return null;
        }

        private List<Scope> RouteScopes(InvocationExpressionSyntax map, SemanticModel model)
        {
            var scopes = new List<Scope>();
            ExpressionSyntax? current = map;
            var visited = new HashSet<RouteKey>();
            while (current is not null)
            {
                var owner = RouteOwner(current, model);
                if (owner is not null && visited.Add(owner) && _routeScopes.TryGetValue(owner, out var scope))
                    scopes.Add(scope);
                current = Unwrap(current);
                if (current is InvocationExpressionSyntax invocation)
                    current = InvocationReceiver(invocation, model);
                else if (GetReferencedSymbol(current, model) is { } symbol && _initializers.TryGetValue(symbol, out var initializer))
                {
                    current = initializer;
                    model = _compilation.GetSemanticModel(current.SyntaxTree);
                }
                else
                    break;
            }
            return scopes;
        }

        private RouteKey? RouteOwner(ExpressionSyntax? expression, SemanticModel model, int depth = 0)
        {
            if (depth > 20)
                return null;
            expression = Unwrap(expression);
            if (expression is InvocationExpressionSyntax invocation)
            {
                if (IsLibrary(GetInvokedMethod(invocation, model), "ErrorPolicyEndpointExtensions", "WithErrorVisibility")
                    || IsBuilderConvention(GetInvokedMethod(invocation, model))
                    || IsUnknownBuilderConvention(invocation, model))
                    return RouteOwner(InvocationReceiver(invocation, model), model, depth + 1);

                return new RouteKey(invocation.SyntaxTree, invocation.SpanStart);
            }
            if (GetReferencedSymbol(expression, model) is { } symbol && _initializers.TryGetValue(symbol, out var initializer))
                return RouteOwner(initializer, _compilation.GetSemanticModel(initializer.SyntaxTree), depth + 1);

            return null;
        }

        private bool HasUnknownConvention(ExpressionSyntax? expression, SemanticModel model, int depth = 0)
        {
            if (depth > 20)
                return true;
            expression = Unwrap(expression);
            if (expression is InvocationExpressionSyntax invocation)
            {
                if (IsUnknownBuilderConvention(invocation, model))
                    return true;

                if (IsBuilderConvention(GetInvokedMethod(invocation, model))
                    || IsLibrary(GetInvokedMethod(invocation, model), "ErrorPolicyEndpointExtensions", "WithErrorVisibility"))
                    return HasUnknownConvention(InvocationReceiver(invocation, model), model, depth + 1);

                return false;
            }
            return GetReferencedSymbol(expression, model) is { } symbol && _initializers.TryGetValue(symbol, out var initializer)
                && HasUnknownConvention(initializer, _compilation.GetSemanticModel(initializer.SyntaxTree), depth + 1);
        }

        private static bool IsUnknownBuilderConvention(InvocationExpressionSyntax invocation, SemanticModel model)
        {
            var method = GetInvokedMethod(invocation, model);
            if (method is null || IsMap(method) || IsControllerMap(method) || method.Name == "MapGroup"
                || IsBuilderConvention(method) || IsLibrary(method, "ErrorPolicyEndpointExtensions", "WithErrorVisibility"))
                return false;

            return Receiver(invocation) is { } receiver && model.GetTypeInfo(receiver).Type is INamedTypeSymbol builder
                && (builder.ToDisplayString() == "Microsoft.AspNetCore.Builder.IEndpointConventionBuilder"
                    || builder.AllInterfaces.Any(contract => contract.ToDisplayString() == "Microsoft.AspNetCore.Builder.IEndpointConventionBuilder"));
        }

        private Application? ApplicationFor(ExpressionSyntax? expression, SemanticModel model, int depth = 0)
        {
            if (depth > 20 || Unwrap(expression) is not { } value)
                return null;

            if (value is InvocationExpressionSyntax invocation)
            {
                var method = GetInvokedMethod(invocation, model);
                if (method is null || !IsFrameworkConfiguration(method) && !IsLibraryConfiguration(method))
                    return null;

                return ApplicationFor(InvocationReceiver(invocation, model), model, depth + 1);
            }

            if (value is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Services"
                && model.GetSymbolInfo(member, _cancellation).Symbol is IPropertySymbol services
                && services.Type.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.IServiceCollection"
                && services.ContainingAssembly.Name.StartsWith("Microsoft.", StringComparison.Ordinal))
                return ApplicationFor(member.Expression, model, depth + 1);

            var symbol = GetReferencedSymbol(value, model);
            if (symbol is not (ILocalSymbol or IFieldSymbol or IParameterSymbol))
                return null;

            if (!_initializers.TryGetValue(symbol, out var initializer))
                return GetApplication(symbol);

            var initializerModel = _compilation.GetSemanticModel(initializer.SyntaxTree);
            if (initializer is InvocationExpressionSyntax creation && GetInvokedMethod(creation, initializerModel) is { IsStatic: true } factory
                && IsType(factory.ContainingType, "Microsoft.AspNetCore.Builder.WebApplication")
                && factory.Name is "CreateBuilder" or "CreateSlimBuilder" or "CreateEmptyBuilder" or "Create")
                return GetApplication(symbol);

            return ApplicationFor(initializer, initializerModel, depth + 1);
        }

        private static bool IsFrameworkConfiguration(IMethodSymbol method) =>
            method.ContainingAssembly.Name == "Microsoft.AspNetCore"
            || method.ContainingAssembly.Name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
            || method.ContainingAssembly.Name.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal);

        private static bool IsLibraryConfiguration(IMethodSymbol method) =>
            IsLibrary(method, "ErrorPolicyServiceExtensions", "AddFlowOnRailsVisibility")
            || IsLibrary(method, "TranslationServiceExtensions", "AddFlowOnRailsTranslation")
            || IsLibrary(method, "ErrorPolicyEndpointExtensions", "WithErrorVisibility")
            || IsGeneratedCatalog(method);

        private void ReadUnknownMutations()
        {
            foreach (var (call, model) in _calls)
            {
                var method = GetInvokedMethod(call, model);
                if (method is null || method.ContainingAssembly.Name == "FlowOnRails.AspNetCore"
                    || IsGeneratedCatalog(method)
                    || method.ContainingAssembly.Name.StartsWith("Microsoft.", StringComparison.Ordinal)
                    || method.ContainingAssembly.Name.StartsWith("System.", StringComparison.Ordinal))
                    continue;

                foreach (var candidate in ConfigurationArguments(call))
                {
                    if (!IsApplicationObject(model.GetTypeInfo(candidate, _cancellation).Type))
                        continue;
                    MarkUnknownConfiguration(candidate, model);
                    Report(call, "application configuration escapes to a helper whose registrations are not statically known");
                }
            }

            foreach (var tree in _compilation.SyntaxTrees)
            {
                if (tree.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var model = _compilation.GetSemanticModel(tree);
                foreach (var assignment in tree.GetRoot(_cancellation).DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    foreach (var candidate in new[] { assignment.Left, assignment.Right })
                        if (IsApplicationObject(model.GetTypeInfo(candidate, _cancellation).Type))
                        {
                            MarkUnknownConfiguration(candidate, model);
                            Report(assignment, "an application, service collection, or route-group alias is reassigned");
                        }
            }
        }

        private static IEnumerable<ExpressionSyntax> ConfigurationArguments(InvocationExpressionSyntax call)
        {
            if (Receiver(call) is { } receiver)
                yield return receiver;

            foreach (var argument in call.ArgumentList.Arguments)
                yield return argument.Expression;
        }

        private void MarkUnknownConfiguration(ExpressionSyntax expression, SemanticModel model)
        {
            var type = model.GetTypeInfo(expression, _cancellation).Type?.ToDisplayString();
            var isRouteBuilder = type is
                "Microsoft.AspNetCore.Routing.RouteGroupBuilder" or "Microsoft.AspNetCore.Builder.RouteHandlerBuilder";

            if (isRouteBuilder && RouteOwner(expression, model) is { } owner)
            {
                if (!_routeScopes.TryGetValue(owner, out var scope))
                    _routeScopes.Add(owner, scope = new Scope());

                scope.Unknown = true;
                return;
            }

            if (ApplicationFor(expression, model) is not { } app)
                return;

            app.Global.Unknown = true;
            app.CatalogUnknown = true;
        }

        private static bool IsApplicationObject(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol named)
                return false;

            if (named.ToDisplayString() is
                "Microsoft.Extensions.DependencyInjection.IServiceCollection"
                or "Microsoft.AspNetCore.Builder.WebApplicationBuilder"
                or "Microsoft.AspNetCore.Builder.WebApplication"
                or "Microsoft.AspNetCore.Routing.RouteGroupBuilder"
                or "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"
                or "Microsoft.AspNetCore.Builder.RouteHandlerBuilder")
                return true;

            return named.AllInterfaces.Any(contract => contract.ToDisplayString() is
                "Microsoft.Extensions.DependencyInjection.IServiceCollection"
                or "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder");
        }

        private bool IsDeferred(SyntaxNode call)
        {
            if (call.Ancestors().Any(node => node is LambdaExpressionSyntax or LocalFunctionStatementSyntax or FieldDeclarationSyntax))
                return true;

            var method = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method is not null)
                return !SameSymbol(_compilation.GetSemanticModel(method.SyntaxTree).GetDeclaredSymbol(method, _cancellation),
                    _compilation.GetEntryPoint(_cancellation));

            return !call.Ancestors().Any(node => node is GlobalStatementSyntax);
        }

        private static bool IsConditional(SyntaxNode call)
        {
            foreach (var ancestor in call.Ancestors())
            {
                if (ancestor is MethodDeclarationSyntax or LocalFunctionStatementSyntax or LambdaExpressionSyntax)
                    return false;

                if (ancestor is
                    IfStatementSyntax or SwitchStatementSyntax or SwitchExpressionSyntax
                    or ConditionalExpressionSyntax or ForStatementSyntax or ForEachStatementSyntax
                    or WhileStatementSyntax or DoStatementSyntax or TryStatementSyntax)
                    return true;
            }

            return false;
        }

        private Application GetApplication(ISymbol symbol)
        {
            if (!_applications.TryGetValue(symbol, out var application))
                _applications.Add(symbol, application = new Application());

            return application;
        }

        private static bool SameNode(SyntaxNode? first, SyntaxNode? second) =>
            first?.SyntaxTree == second?.SyntaxTree && first?.Span == second?.Span;

        private static SyntaxNode? FunctionOwner(SyntaxNode node) => node.Ancestors().FirstOrDefault(ancestor => ancestor is
            AnonymousFunctionExpressionSyntax or MethodDeclarationSyntax or LocalFunctionStatementSyntax);

        private static bool IsReturned(InvocationExpressionSyntax call, SyntaxNode body)
        {
            if (!SameNode(FunctionOwner(call), body))
                return false;

            SyntaxNode expression = call;
            while (expression.Parent is ParenthesizedExpressionSyntax or ConditionalExpressionSyntax or AwaitExpressionSyntax
                || (expression.Parent is BinaryExpressionSyntax binary
                    && binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceExpression)))
                expression = expression.Parent;

            return expression.Parent is ArrowExpressionClauseSyntax or AnonymousFunctionExpressionSyntax or ReturnStatementSyntax;
        }

        private static bool ScopeReceiver(ExpressionSyntax? expression, ISymbol parameter, bool policy, SemanticModel model)
        {
            expression = Unwrap(expression);
            if (!policy)
                return SameSymbol(GetReferencedSymbol(expression, model), parameter);

            return expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Global"
                && SameSymbol(GetReferencedSymbol(member.Expression, model), parameter)
                && model.GetSymbolInfo(member).Symbol is IPropertySymbol property
                && IsType(property.ContainingType, "FlowOnRails.AspNetCore.Visibility.PolicyBuilder");
        }

        private bool Within(Selection selection, Selection? bounds)
        {
            if (bounds is null)
                return true;

            if (Includes(bounds, selection.Root))
            {
                var selectsExcludedRoot = selection.Range == SelectionRange.Tree
                    && bounds.Range == SelectionRange.Descendants
                    && SameType(selection.Root, bounds.Root);

                if (!selectsExcludedRoot)
                    return true;
            }

            return bounds.Range == SelectionRange.Descendants
                && selection.Range == SelectionRange.Descendants
                && SameType(bounds.Root, selection.Root);
        }

        private bool Includes(Selection selection, ITypeSymbol type)
        {
            if (SameType(selection.Root, type))
                return selection.Range != SelectionRange.Descendants;

            if (selection.Range == SelectionRange.Exact)
                return false;

            for (var parent = (type as INamedTypeSymbol)?.BaseType; parent is not null; parent = parent.BaseType)
                if (SameType(parent, selection.Root))
                    return true;

            return false;
        }

        private static bool IsControllerAction(IMethodSymbol method)
        {
            if (method.DeclaredAccessibility != Accessibility.Public || method.IsStatic)
                return false;

            if (method.GetAttributes().Any(attribute =>
                IsType(attribute.AttributeClass, "Microsoft.AspNetCore.Mvc.NonActionAttribute")))
                return false;

            var controller = method.ContainingType;

            if (controller.GetAttributes().Any(attribute =>
                IsType(attribute.AttributeClass, "Microsoft.AspNetCore.Mvc.NonControllerAttribute")))
                return false;

            if (controller.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
                return true;

            if (controller.GetAttributes().Any(attribute =>
                IsType(attribute.AttributeClass, "Microsoft.AspNetCore.Mvc.ControllerAttribute")))
                return true;

            return Ancestors(controller).Any(type =>
                IsType(type, "Microsoft.AspNetCore.Mvc.ControllerBase"));
        }

        private static IEnumerable<INamedTypeSymbol> Ancestors(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
                yield return current;
        }

        private static bool IsGeneratedCatalog(IMethodSymbol? method)
        {
            if (method is null || method.Name != "Add" + method.ContainingType.Name)
                return false;

            return method.DeclaringSyntaxReferences.Any(reference =>
                reference.SyntaxTree.FilePath.EndsWith(".Registration.g.cs", StringComparison.Ordinal));
        }

        private static bool IsBuilderConvention(IMethodSymbol? method)
        {
            if (method is null
                || !method.ContainingAssembly.Name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal))
                return false;

            return method.Name is
                "WithName" or "WithTags" or "RequireAuthorization" or "AllowAnonymous"
                or "WithSummary" or "WithDescription" or "WithDisplayName"
                or "Produces" or "ProducesProblem" or "WithMetadata"
                or "ExcludeFromDescription" or "WithOpenApi" or "WithGroupName";
        }

        private static bool IsControllerMap(IMethodSymbol? method)
        {
            if (method is null
                || !method.ContainingAssembly.Name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal))
                return false;

            return method.Name is
                "MapControllers" or "MapControllerRoute"
                or "MapDefaultControllerRoute" or "MapAreaControllerRoute";
        }

        private static bool IsMap(IMethodSymbol? method)
        {
            if (method is null
                || method.ContainingNamespace.ToDisplayString() != "Microsoft.AspNetCore.Builder"
                || !method.ContainingAssembly.Name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal))
                return false;

            return method.Name is
                "MapGet" or "MapPost" or "MapPut" or "MapPatch"
                or "MapDelete" or "MapMethods" or "Map";
        }

        private static bool IsConverter(IMethodSymbol? method)
        {
            if (method is null
                || method.ContainingAssembly.Name != "FlowOnRails.AspNetCore"
                || method.ContainingNamespace.ToDisplayString() != "FlowOnRails.AspNetCore.Translation")
                return false;

            return method.Name is "ToActionResult" or "ToHttpResult";
        }

        private static bool IsLibrary(IMethodSymbol? method, string typeName, string methodName)
        {
            if (method is null
                || method.Name != methodName
                || method.ContainingAssembly.Name != "FlowOnRails.AspNetCore")
                return false;

            return method.ContainingType.Name == typeName
                || method.ContainingType.ContainingType?.Name == typeName;
        }

        private static bool IsScopeType(INamedTypeSymbol type) =>
            IsType(type, "FlowOnRails.AspNetCore.Visibility.RuleScope")
            || IsType(type, "FlowOnRails.AspNetCore.Visibility.RuleTree");

        private static bool IsType(INamedTypeSymbol? type, string name) =>
            type?.ToDisplayString() == name;

        private bool SameType(ITypeSymbol first, ITypeSymbol second) =>
            _compilation.ClassifyCommonConversion(first, second).IsIdentity;

        private static bool SameSymbol(ISymbol? first, ISymbol? second) =>
            SymbolEqualityComparer.Default.Equals(first, second);

        private static IMethodSymbol? GetInvokedMethod(InvocationExpressionSyntax call, SemanticModel model) =>
            model.GetSymbolInfo(call).Symbol as IMethodSymbol;

        private static IMethodSymbol? HandlerMethod(ExpressionSyntax handler, SemanticModel model)
        {
            if (model.GetSymbolInfo(handler).Symbol is IMethodSymbol method)
                return method;

            var operation = model.GetOperation(handler);
            if (operation is IDelegateCreationOperation creation)
                operation = creation.Target;
            if (operation is IMethodReferenceOperation reference)
                return reference.Method;

            var candidates = model.GetSymbolInfo(handler).CandidateSymbols.OfType<IMethodSymbol>().ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }

        private static ISymbol? GetReferencedSymbol(ExpressionSyntax? expression, SemanticModel model) =>
            expression is null ? null : model.GetSymbolInfo(expression).Symbol;

        private static ExpressionSyntax? InvocationReceiver(InvocationExpressionSyntax call, SemanticModel model)
        {
            if (model.GetOperation(call) is not IInvocationOperation operation)
                return null;

            // Static extension calls bind the receiver as the first parameter.
            var receiver = operation.Instance
                ?? operation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
            return receiver?.Syntax as ExpressionSyntax;
        }

        private static ExpressionSyntax? Receiver(InvocationExpressionSyntax call) =>
            call.Expression is MemberAccessExpressionSyntax member ? member.Expression : null;

        private static ExpressionSyntax? LastArgument(InvocationExpressionSyntax call) =>
            call.ArgumentList.Arguments.LastOrDefault()?.Expression;

        private static ExpressionSyntax? Unwrap(ExpressionSyntax? expression)
        {
            while (expression is ParenthesizedExpressionSyntax parentheses)
                expression = parentheses.Expression;

            return expression;
        }

        private static ISymbol? LambdaParameter(LambdaExpressionSyntax? lambda, SemanticModel model) => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => model.GetDeclaredSymbol(simple.Parameter),
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized =>
                model.GetDeclaredSymbol(parenthesized.ParameterList.Parameters[0]),
            _ => null
        };

        private static IEnumerable<SyntaxNode> Statements(LambdaExpressionSyntax lambda)
        {
            if (lambda.Body is not BlockSyntax block)
            {
                yield return lambda.Body;
                yield break;
            }

            foreach (var statement in block.Statements)
                yield return statement is ExpressionStatementSyntax expression
                    ? expression.Expression
                    : statement;
        }

        private void Report(SyntaxNode node, string reason)
        {
            var key = (node.SyntaxTree, node.SpanStart, reason);
            if (_reported.Add(key))
                context.ReportDiagnostic(Diagnostic.Create(Unavailable, node.GetLocation(), reason));
        }

        private sealed class Application
        {
            internal Scope Global { get; } = new();
            internal Dictionary<ITypeSymbol, Scope> Controllers { get; } = new(SymbolEqualityComparer.Default);
            internal HashSet<ITypeSymbol> Mappings { get; } = new(SymbolEqualityComparer.Default);

            internal bool HasPolicy;
            internal bool HasCatalog;
            internal bool CatalogUnknown;
        }

        private sealed class Scope
        {
            internal List<Rule> Rules { get; } = [];
            internal bool Unknown;
        }

        private enum SelectionRange
        {
            Exact,
            Descendants,
            Tree
        }

        private enum VisibilityBehavior
        {
            Generic,
            EachType,
            NearestAncestor
        }

        private sealed record RouteKey(SyntaxTree Tree, int Position);

        private sealed record Selection(ITypeSymbol Root, SelectionRange Range);

        private sealed record Rule(Selection Selection, VisibilityBehavior Behavior);
    }
}
