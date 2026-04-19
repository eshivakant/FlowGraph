using System.Xml.Linq;
using System.Text.Json;
using System.Collections.Immutable;
using FlowGraph.Graph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace FlowGraph.Roslyn;

public sealed class DeterministicRoslynIngestor : IRoslynIngestor
{
    public async Task<IReadOnlyList<GraphTriple>> IngestAsync(RoslynIngestionRequest request, Func<string, Task>? progress, CancellationToken cancellationToken)
    {
        using var workspace = MsBuildWorkspaceLoader.CreateWorkspace();

        workspace.WorkspaceFailed += (s, e) =>
        {
            // Report diagnostics to console for now, could be wired to progress if needed.
            Console.WriteLine($"[Roslyn Diagnostic] {e.Diagnostic.Kind}: {e.Diagnostic.Message}");
        };

        var solutionPath = ResolveSolutionPath(request);
        if (solutionPath is null)
        {
            return Array.Empty<GraphTriple>();
        }

        Solution solution;
        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();

        if (ext == ".slnx")
        {
            if (progress != null) await progress($"Parsing .slnx solution: {Path.GetFileName(solutionPath)}...");
            var xml = await File.ReadAllTextAsync(solutionPath, cancellationToken);
            var doc = XDocument.Parse(xml);
            var projectPaths = doc.Descendants("Project")
                .Select(p => new { Path = p.Attribute("Path")?.Value, Name = Path.GetFileNameWithoutExtension(p.Attribute("Path")?.Value ?? "") })
                .Where(x => x.Path != null && !IsTest(x.Name, x.Path))
                .Select(x => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solutionPath)!, x.Path!)))
                .ToList();

            if (progress != null) await progress($"Found {projectPaths.Count} projects in .slnx. Loading...");
            foreach (var p in projectPaths)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        await workspace.OpenProjectAsync(p, cancellationToken: cancellationToken);
                    }
                    catch
                    {

                    }
                }
            }
            solution = workspace.CurrentSolution;
        }
        else if (ext == ".slnf")
        {
            if (progress != null) await progress($"Parsing .slnf solution filter: {Path.GetFileName(solutionPath)}...");
            var json = await File.ReadAllTextAsync(solutionPath, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var solutionInfo = doc.RootElement.GetProperty("solution");
            var baseSlnPath = solutionInfo.GetProperty("path").GetString();
            var projectsInFilter = solutionInfo.GetProperty("projects").EnumerateArray()
                .Select(p => p.GetString())
                .Where(p => p != null)
                .Select(p => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solutionPath)!, p!)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (baseSlnPath == null) throw new InvalidOperationException("Invalid .slnf file: missing base solution path.");
            var fullBaseSlnPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solutionPath)!, baseSlnPath));

            if (progress != null) await progress($"Loading base solution: {Path.GetFileName(fullBaseSlnPath)}...");
            var baseSolution = await workspace.OpenSolutionAsync(fullBaseSlnPath, cancellationToken: cancellationToken);
            
            // Filter the solution to only include projects in the filter
            var projectIdsToRemove = baseSolution.Projects
                .Where(p => p.FilePath == null || !projectsInFilter.Contains(Path.GetFullPath(p.FilePath)))
                .Select(p => p.Id)
                .ToList();

            var filteredSolution = baseSolution;
            foreach (var id in projectIdsToRemove)
            {
                filteredSolution = filteredSolution.RemoveProject(id);
            }
            solution = filteredSolution;
        }
        else
        {
            // Standard .sln path
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

            try
            {
                solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out while trying to open the solution {Path.GetFileName(solutionPath)}.");
            }
        }

        if (progress != null) await progress($"Workspace loaded with {solution.Projects.Count()} projects.");
        var changedFullPaths = request.ChangedFiles
            .Select(f => Path.GetFullPath(Path.Combine(request.RepoRootPath, f)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allTriples = new System.Collections.Concurrent.ConcurrentBag<GraphTriple>();
        int totalFiles = changedFullPaths.Count;
        int processedFiles = 0;

        foreach (var project in solution.Projects)
        {
            if (IsTestProject(project))
            {
                if (progress != null) await progress($"Skipping test project {project.Name}...");
                continue;
            }

            if (progress != null) await progress($"Compiling project {project.Name}...");
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null) continue;

            var docsToProcess = project.Documents
                .Where(d => d.FilePath is null || changedFullPaths.Contains(Path.GetFullPath(d.FilePath)))
                .ToList();

            if (docsToProcess.Count == 0) continue;

            if (progress != null) await progress($"Analyzing {docsToProcess.Count} files in {project.Name}...");

            await Parallel.ForEachAsync(docsToProcess, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken }, async (document, ct) =>
            {
                var tree = await document.GetSyntaxTreeAsync(ct);
                if (tree is null) return;

                var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
                var root = await tree.GetRootAsync(ct);

                var triples = new List<GraphTriple>();
                ExtractCalls(model, root, triples, request);
                ExtractConstructorDependencies(model, root, triples, request);
                ExtractRestEndpoints(model, root, triples, request);
                ExtractMessaging(model, root, triples, request);
                ExtractInterfaces(model, root, triples, request);
                ExtractTypes(model, root, triples, request);

                foreach (var t in triples) allTriples.Add(t);

                var current = Interlocked.Increment(ref processedFiles);
                if (progress != null && current % 10 == 0)
                {
                    await progress($"Roslyn Progress: {current}/{totalFiles} files processed ({allTriples.Count} triples found)...");
                }
            });
        }

        if (progress != null) await progress($"Roslyn ingestion complete. Found {allTriples.Count} triples across {processedFiles} files.");
        return allTriples.ToList();
    }

    private static void ExtractInterfaces(SemanticModel model, SyntaxNode root, List<GraphTriple> triples, RoslynIngestionRequest req)
    {
        // Class implements Interface / Inherits Class
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var classSym = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSym is null) continue;

            // Base class inheritance
            if (classSym.BaseType != null && classSym.BaseType.SpecialType == SpecialType.None)
            {
                triples.Add(new GraphTriple(
                    TypeEntity(classSym, "Class"),
                    GraphRelation.Inherits,
                    TypeEntity(classSym.BaseType, "Class"),
                    new Dictionary<string, object?>
                    {
                        ["repo"] = req.RepoName,
                        ["commit"] = req.CommitSha,
                    }));
            }

            foreach (var iface in classSym.Interfaces)
            {
                triples.Add(new GraphTriple(
                    TypeEntity(classSym, "Class"),
                    GraphRelation.Implements,
                    TypeEntity(iface, "Interface"),
                    new Dictionary<string, object?>
                    {
                        ["repo"] = req.RepoName,
                        ["commit"] = req.CommitSha,
                    }));
            }
        }

        // Interface inherits from Interface
        foreach (var ifaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
        {
            var ifaceSym = model.GetDeclaredSymbol(ifaceDecl) as INamedTypeSymbol;
            if (ifaceSym is null) continue;

            foreach (var baseIface in ifaceSym.Interfaces)
            {
                triples.Add(new GraphTriple(
                    TypeEntity(ifaceSym, "Interface"),
                    GraphRelation.Implements,
                    TypeEntity(baseIface, "Interface"),
                    new Dictionary<string, object?>
                    {
                        ["repo"] = req.RepoName,
                        ["commit"] = req.CommitSha,
                    }));
            }
        }
    }

    private static void ExtractCalls(SemanticModel model, SyntaxNode root, List<GraphTriple> triples, RoslynIngestionRequest req)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null)
            {
                continue;
            }

            var caller = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (caller is null)
            {
                continue;
            }

            var callerSymbol = model.GetDeclaredSymbol(caller) as IMethodSymbol;
            if (callerSymbol is null)
            {
                continue;
            }

            var src = MethodEntity(callerSymbol);
            var dst = MethodEntity(symbol);
            triples.Add(new GraphTriple(
                src,
                GraphRelation.Calls,
                dst,
                new Dictionary<string, object?>
                {
                    ["repo"] = req.RepoName,
                    ["commit"] = req.CommitSha,
                }));
        }
    }

    private static void ExtractConstructorDependencies(SemanticModel model, SyntaxNode root, List<GraphTriple> triples, RoslynIngestionRequest req)
    {
        foreach (var ctor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var ctorSymbol = model.GetDeclaredSymbol(ctor) as IMethodSymbol;
            if (ctorSymbol is null || ctorSymbol.MethodKind != MethodKind.Constructor)
            {
                continue;
            }

            var owningType = ctorSymbol.ContainingType;
            if (owningType is null)
            {
                continue;
            }

            var src = TypeEntity(owningType, kind: "Class");
            foreach (var p in ctorSymbol.Parameters)
            {
                var depType = p.Type;
                if (depType is null)
                {
                    continue;
                }

                var dst = TypeEntity(depType, kind: "Type");
                triples.Add(new GraphTriple(
                    src,
                    GraphRelation.DependsOn,
                    dst,
                    new Dictionary<string, object?>
                    {
                        ["repo"] = req.RepoName,
                        ["commit"] = req.CommitSha,
                        ["parameter"] = p.Name,
                    }));
            }
        }
    }

    private static void ExtractRestEndpoints(SemanticModel model, SyntaxNode root, List<GraphTriple> triples, RoslynIngestionRequest req)
    {
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var typeSym = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (typeSym is null)
            {
                continue;
            }

            if (!InheritsFromControllerBase(typeSym))
            {
                continue;
            }

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodSym = model.GetDeclaredSymbol(method) as IMethodSymbol;
                if (methodSym is null)
                {
                    continue;
                }

                foreach (var attr in method.AttributeLists.SelectMany(a => a.Attributes))
                {
                    var attrSym = model.GetSymbolInfo(attr).Symbol?.ContainingType;
                    var attrName = attrSym?.Name;
                    var http = attrName switch
                    {
                        "HttpGetAttribute" => "GET",
                        "HttpPostAttribute" => "POST",
                        "HttpPutAttribute" => "PUT",
                        "HttpDeleteAttribute" => "DELETE",
                        _ => null
                    };

                    if (http is null)
                    {
                        continue;
                    }

                    var route = attr.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax lit &&
                                lit.IsKind(SyntaxKind.StringLiteralExpression)
                        ? lit.Token.ValueText
                        : null;

                    var endpointId = $"{http} {typeSym.Name}.{methodSym.Name}";
                    var endpoint = new GraphEntity("Endpoint", endpointId, new Dictionary<string, object?>
                    {
                        ["http"] = http,
                        ["route"] = route,
                        ["controller"] = typeSym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ["method"] = MethodId(methodSym),
                        ["repo"] = req.RepoName,
                    });

                    triples.Add(new GraphTriple(
                        endpoint,
                        GraphRelation.Triggers,
                        MethodEntity(methodSym),
                        new Dictionary<string, object?>
                        {
                            ["repo"] = req.RepoName,
                            ["commit"] = req.CommitSha,
                        }));
                }
            }
        }
    }

    private static void ExtractMessaging(SemanticModel model, SyntaxNode root, List<GraphTriple> triples, RoslynIngestionRequest req)
    {
        static bool IsPublishLike(string name) =>
            name is "Publish" or "Send" or "Emit" or "Produce";

        static bool IsConsumeLike(string name) =>
            name is "Subscribe" or "Consume" or "Handle";

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null)
            {
                continue;
            }

            var callerDecl = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            var callerSym = callerDecl is null ? null : model.GetDeclaredSymbol(callerDecl) as IMethodSymbol;
            if (callerSym is null)
            {
                continue;
            }

            var name = symbol.Name;
            if (!IsPublishLike(name) && !IsConsumeLike(name))
            {
                // Also cover Bus.Publish(...)
                if (!(name == "Publish" && symbol.ContainingType?.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase) == true))
                {
                    continue;
                }
            }

            var messageType = ResolveMessageType(symbol, invocation, model);
            if (messageType is null)
            {
                continue;
            }

            var message = new GraphEntity("Message", messageType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), new Dictionary<string, object?>
            {
                ["name"] = messageType.Name,
                ["namespace"] = messageType.ContainingNamespace?.ToDisplayString(),
            });

            var topic = ResolveTopic(invocation);
            if (IsPublishLike(name) || (name == "Publish" && symbol.ContainingType?.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase) == true))
            {
                var src = MethodEntity(callerSym);
                triples.Add(new GraphTriple(src, GraphRelation.Publishes, message, new Dictionary<string, object?>
                {
                    ["repo"] = req.RepoName,
                    ["commit"] = req.CommitSha,
                }));

                if (topic is not null)
                {
                    triples.Add(new GraphTriple(src, GraphRelation.UsesTopic, new GraphEntity("Topic", topic, new Dictionary<string, object?> { ["name"] = topic }), new Dictionary<string, object?>
                    {
                        ["repo"] = req.RepoName,
                        ["commit"] = req.CommitSha,
                    }));
                }
            }
            else if (IsConsumeLike(name))
            {
                var handler = MethodEntity(callerSym);
                triples.Add(new GraphTriple(handler, GraphRelation.Handles, message, new Dictionary<string, object?>
                {
                    ["repo"] = req.RepoName,
                    ["commit"] = req.CommitSha,
                }));

                if (topic is not null)
                {
                    triples.Add(new GraphTriple(handler, GraphRelation.UsesTopic, new GraphEntity("Topic", topic, new Dictionary<string, object?> { ["name"] = topic }), new Dictionary<string, object?>
                    {
                        ["repo"] = req.RepoName,
                        ["commit"] = req.CommitSha,
                    }));
                }
            }
        }
    }

    private static INamedTypeSymbol? ResolveMessageType(IMethodSymbol symbol, InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (symbol.IsGenericMethod && symbol.TypeArguments.Length == 1 && symbol.TypeArguments[0] is INamedTypeSymbol nts)
        {
            return nts;
        }

        // Fallback: first argument type
        var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (firstArg is null)
        {
            return null;
        }

        var typeInfo = model.GetTypeInfo(firstArg.Expression).Type as INamedTypeSymbol;
        return typeInfo;
    }

    private static string? ResolveTopic(InvocationExpressionSyntax invocation)
    {
        // Deterministic MVP: only literal strings. Constants/config resolution can be layered in later.
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return lit.Token.ValueText;
            }
        }

        return null;
    }

    private static bool InheritsFromControllerBase(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.Name == "ControllerBase")
            {
                return true;
            }
        }
        return false;
    }

    private static GraphEntity MethodEntity(IMethodSymbol method) =>
        new("Method", MethodId(method), new Dictionary<string, object?>
        {
            ["name"] = method.Name,
            ["type"] = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        });

    private static GraphEntity TypeEntity(ITypeSymbol type, string kind) =>
        new(kind, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), new Dictionary<string, object?>
        {
            ["name"] = type.Name,
            ["namespace"] = type.ContainingNamespace?.ToDisplayString(),
        });

    private void ExtractTypes(SemanticModel model, SyntaxNode root, List<GraphTriple> triples, RoslynIngestionRequest request)
    {
        var types = root.DescendantNodesAndSelf().OfType<TypeDeclarationSyntax>();
        foreach (var type in types)
        {
            var symbol = model.GetDeclaredSymbol(type);
            if (symbol == null) continue;

            var typeId = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var typeEntity = new GraphEntity("Type", typeId, new Dictionary<string, object?>
            {
                ["name"] = symbol.Name,
                ["kind"] = type.Kind().ToString(),
                ["fullName"] = symbol.ToDisplayString()
            });

            // We add a self-triple or just ensure the node exists. 
            // In our system, nodes are created via triples. 
            // To ensure the class exists even if it has no members, we could add a metadata triple, 
            // but usually linking it to its members is enough.
            
            foreach (var member in symbol.GetMembers())
            {
                if (member is IMethodSymbol method && !method.IsImplicitlyDeclared)
                {
                    var methodId = MethodId(method);
                    var methodEntity = new GraphEntity("Method", methodId, new Dictionary<string, object?>
                    {
                        ["name"] = method.Name,
                        ["fullName"] = method.ToDisplayString()
                    });

                    triples.Add(new GraphTriple(typeEntity, GraphRelation.Contains, methodEntity, new Dictionary<string, object?>()));
                }
            }
        }
    }

    private static string MethodId(IMethodSymbol method)
    {
        var containing = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "<unknown>";
        var parameters = string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        return $"{containing}.{method.Name}({parameters})";
    }

    private static string? FindSingleSolution(string repoRoot)
    {
        var slns = Directory.GetFiles(repoRoot, "*.sln", SearchOption.AllDirectories);
        return slns.Length == 1 ? slns[0] : slns.FirstOrDefault();
    }

    private static string? ResolveSolutionPath(RoslynIngestionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SolutionPath))
        {
            var provided = request.SolutionPath!;

            // If user provided a relative path, treat it as relative to the cloned repo root.
            var candidate = Path.IsPathRooted(provided)
                ? provided
                : Path.GetFullPath(Path.Combine(request.RepoRootPath, provided));

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fallback: auto-discover a solution inside the cloned repo.
        return FindSingleSolution(request.RepoRootPath);
    }

    private static bool IsTestProject(Project project)
    {
        return IsTest(project.Name, project.FilePath ?? "");
    }

    private static bool IsTest(string name, string path)
    {
        var n = name.ToLowerInvariant();
        var p = path.ToLowerInvariant();

        return n.Contains(".test") ||
               n.Contains(".tests") ||
               n.Contains("unittest") ||
               n.Contains("integrationtest") ||
               p.Contains("/tests/") ||
               p.Contains("\\tests\\") ||
               p.Contains("/test/") ||
               p.Contains("\\test\\");
    }
}

