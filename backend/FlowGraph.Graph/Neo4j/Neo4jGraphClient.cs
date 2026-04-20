using Neo4j.Driver;
using Microsoft.Extensions.Logging;

namespace FlowGraph.Graph.Neo4j;

public sealed class Neo4jGraphClient : IGraphWriter, IGraphQueryService, IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly ILogger<Neo4jGraphClient> _logger;
    private readonly string? _database;
    private readonly string _connectionName;

    public Neo4jGraphClient(string connectionName, string uri, string username, string password, string? database, ILogger<Neo4jGraphClient> logger)
    {
        _connectionName = connectionName;
        _database = string.IsNullOrWhiteSpace(database) ? null : database.Trim();
        _logger = logger;
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
        _logger.LogInformation("Neo4j graph client initialized for connection '{ConnectionName}' (database: {Database}).", _connectionName, _database ?? "(default)");
    }

    public async Task UpsertAsync(string repoName, string commitSha, IReadOnlyList<GraphTriple> triples, Func<string, Task>? progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Upserting {TripleCount} triples to Neo4j.", triples.Count);
        if (triples.Count == 0)
        {
            return;
        }

        if (progress != null) await progress($"Preparing to write {triples.Count} triples to Neo4j...");

        var groups = triples.GroupBy(t => t.Relation).ToList();
        int processed = 0;

        foreach (var group in groups)
        {
            var rel = RelationToCypher(group.Key);
            var allTriplesInGroup = group.Select(t => new
            {
                sk = t.Source.Kind,
                sid = t.Source.Id,
                sprops = t.Source.Properties.ToDictionary(kv => kv.Key, kv => kv.Value),
                tk = t.Target.Kind,
                tid = t.Target.Id,
                tprops = t.Target.Properties.ToDictionary(kv => kv.Key, kv => kv.Value),
                rprops = t.Properties.ToDictionary(kv => kv.Key, kv => kv.Value),
            }).ToList();

            const int BatchSize = 1000;
            var batches = allTriplesInGroup.Chunk(BatchSize);

            foreach (var batch in batches)
            {
                await using var session = CreateSession(AccessMode.Write);
                await session.ExecuteWriteAsync(async tx =>
                {
                    var cypher = $@"
                        UNWIND $batch AS t
                        MERGE (s:Entity {{ kind: t.sk, id: t.sid }})
                        SET s += t.sprops
                        MERGE (target:Entity {{ kind: t.tk, id: t.tid }})
                        SET target += t.tprops
                        MERGE (s)-[r:{rel} {{ repo: $repo }}]->(target)
                        SET r += t.rprops
                        SET r.commit = $commit";

                    await tx.RunAsync(cypher, new { batch, repo = repoName, commit = commitSha });
                });

                processed += batch.Length;
                if (progress != null) await progress($"Progress: {processed}/{triples.Count} triples written (last batch: {batch.Length} of {rel}).");
            }
        }

        _logger.LogInformation("Neo4j upsert completed.");
    }

    public async Task DeleteRepoAsync(string repoName, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Deleting repository graph data.");
        await using var session = CreateSession(AccessMode.Write);

        bool hasMore = true;
        while (hasMore && !cancellationToken.IsCancellationRequested)
        {
            var deletedCount = await session.ExecuteWriteAsync(async tx =>
            {
                var res = await tx.RunAsync(@"
                    MATCH ()-[r {repo: $repo}]->()
                    WITH r LIMIT 10000
                    DELETE r
                    RETURN count(r) as count", new { repo = repoName });
                var record = await res.SingleAsync();
                return record["count"].As<int>();
            });
            hasMore = deletedCount > 0;
        }

        hasMore = true;
        while (hasMore && !cancellationToken.IsCancellationRequested)
        {
            var deletedCount = await session.ExecuteWriteAsync(async tx =>
            {
                var res = await tx.RunAsync(@"
                    MATCH (n:Entity)
                    WHERE NOT (n)-[]-()
                    WITH n LIMIT 10000
                    DELETE n
                    RETURN count(n) as count");
                var record = await res.SingleAsync();
                return record["count"].As<int>();
            });
            hasMore = deletedCount > 0;
        }
        _logger.LogWarning("Finished deleting repository graph data.");
    }

    public async Task WipeAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("Wiping all graph data.");
        await using var session = CreateSession(AccessMode.Write);

        bool hasMore = true;
        while (hasMore && !cancellationToken.IsCancellationRequested)
        {
            var deletedCount = await session.ExecuteWriteAsync(async tx =>
            {
                var res = await tx.RunAsync(@"
                    MATCH ()-[r]->()
                    WITH r LIMIT 10000
                    DELETE r
                    RETURN count(r) as count");
                var record = await res.SingleAsync();
                return record["count"].As<int>();
            });
            hasMore = deletedCount > 0;
        }

        hasMore = true;
        while (hasMore && !cancellationToken.IsCancellationRequested)
        {
            var deletedCount = await session.ExecuteWriteAsync(async tx =>
            {
                var res = await tx.RunAsync(@"
                    MATCH (n)
                    WITH n LIMIT 10000
                    DELETE n
                    RETURN count(n) as count");
                var record = await res.SingleAsync();
                return record["count"].As<int>();
            });
            hasMore = deletedCount > 0;
        }
        _logger.LogWarning("Completed graph wipe.");
    }

    public async Task<IReadOnlyList<GraphEntity>> SearchAsync(string query, string[]? repos, int take, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running graph search with take={Take}.", take);
        await using var session = CreateSession(AccessMode.Read);
        var repoList = (repos == null || repos.Length == 0) ? null : repos;
        var cursor = await session.RunAsync(
            """
            MATCH (e:Entity)
            WHERE (toLower(e.id) CONTAINS toLower($q)
               OR (e.name IS NOT NULL AND toLower(e.name) CONTAINS toLower($q)))
            AND ($repos IS NULL OR EXISTS {
                MATCH (e)-[r]-() WHERE r.repo IN $repos
            })
            RETURN e.kind AS kind, e.id AS id, properties(e) AS props
            LIMIT $take
            """,
            new { q = query, take, repos = repoList });

        var list = new List<GraphEntity>();
        while (await cursor.FetchAsync())
        {
            var kind = cursor.Current["kind"].As<string>();
            var id = cursor.Current["id"].As<string>();
            var props = cursor.Current["props"].As<Dictionary<string, object?>>();
            list.Add(new GraphEntity(kind, id, props));
        }

        _logger.LogInformation("Graph search returned {Count} results.", list.Count);
        return list;
    }

    public async Task<TraceResult> TraceAsync(string startId, string[]? repos, int maxDepth, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running graph trace with depth {MaxDepth}.", maxDepth);
        await using var session = CreateSession(AccessMode.Read);

        var nodeCursor = await session.RunAsync(
            "MATCH (s:Entity) WHERE toLower(s.id) = toLower($start) RETURN s.kind AS kind, s.id AS id, properties(s) AS props",
            new { start = startId });

        GraphEntity? startNode = null;
        if (await nodeCursor.FetchAsync())
        {
            startNode = new GraphEntity(
                nodeCursor.Current["kind"].As<string>(),
                nodeCursor.Current["id"].As<string>(),
                nodeCursor.Current["props"].As<Dictionary<string, object?>>());
        }

        if (startNode == null)
        {
            _logger.LogWarning("Trace start node not found.");
            return new TraceResult(null, Array.Empty<GraphTriple>());
        }

        var repoList = (repos == null || repos.Length == 0) ? null : repos;
        var cursor = await session.RunAsync(
            $$"""
            MATCH (s:Entity) WHERE toLower(s.id) = toLower($start)
            MATCH p=(s)-[*1..{{maxDepth}}]-(t:Entity)
            WHERE ALL(rel IN relationships(p) WHERE $repos IS NULL OR rel.repo IN $repos)
            UNWIND relationships(p) AS r
            WITH startNode(r) AS a, type(r) AS rt, endNode(r) AS b, properties(r) AS rp
            RETURN DISTINCT a.kind AS ak, a.id AS aid, properties(a) AS ap, rt AS relType, b.kind AS bk, b.id AS bid, properties(b) AS bp, rp AS rp
            LIMIT 500
            """,
            new { start = startId, repos = repoList });

        var results = new List<GraphTriple>();
        while (await cursor.FetchAsync())
        {
            var src = new GraphEntity(
                cursor.Current["ak"].As<string>(),
                cursor.Current["aid"].As<string>(),
                cursor.Current["ap"].As<Dictionary<string, object?>>());
            var tgt = new GraphEntity(
                cursor.Current["bk"].As<string>(),
                cursor.Current["bid"].As<string>(),
                cursor.Current["bp"].As<Dictionary<string, object?>>());
            var rel = CypherToRelation(cursor.Current["relType"].As<string>());
            var rp = cursor.Current["rp"].As<Dictionary<string, object?>>();
            results.Add(new GraphTriple(src, rel, tgt, rp));
        }

        _logger.LogInformation("Trace completed with {Count} triples.", results.Count);
        return new TraceResult(startNode, results);
    }

    public async Task<IReadOnlyList<GraphEntity>> ImpactAsync(string changeId, string[]? repos, int maxDepth, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running impact analysis with depth {MaxDepth}.", maxDepth);
        await using var session = CreateSession(AccessMode.Read);
        var repoList = (repos == null || repos.Length == 0) ? null : repos;
        var cursor = await session.RunAsync(
            $$"""
            MATCH (c:Entity)
            WHERE toLower(c.id) = toLower($id)
            WITH c
            MATCH p=(c)-[*1..{{maxDepth}}]-(e:Entity)
            WHERE ALL(rel IN relationships(p) WHERE $repos IS NULL OR rel.repo IN $repos)
            RETURN DISTINCT e.kind AS kind, e.id AS id, properties(e) AS props
            LIMIT 500
            """,
            new { id = changeId, repos = repoList });

        var list = new List<GraphEntity>();
        while (await cursor.FetchAsync())
        {
            var kind = cursor.Current["kind"].As<string>();
            var id = cursor.Current["id"].As<string>();
            var props = cursor.Current["props"].As<Dictionary<string, object?>>();
            list.Add(new GraphEntity(kind, id, props));
        }
        _logger.LogInformation("Impact analysis returned {Count} entities.", list.Count);
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing Neo4j graph client for connection '{ConnectionName}'.", _connectionName);
        await _driver.DisposeAsync();
    }

    private IAsyncSession CreateSession(AccessMode mode)
    {
        return _driver.AsyncSession(options =>
        {
            options.WithDefaultAccessMode(mode);
            if (!string.IsNullOrWhiteSpace(_database))
            {
                options.WithDatabase(_database);
            }
        });
    }

    private static string RelationToCypher(GraphRelation relation) =>
        relation switch
        {
            GraphRelation.Calls => "CALLS",
            GraphRelation.Publishes => "PUBLISHES",
            GraphRelation.Consumes => "CONSUMES",
            GraphRelation.Handles => "HANDLES",
            GraphRelation.DependsOn => "DEPENDS_ON",
            GraphRelation.UsesTopic => "USES_TOPIC",
            GraphRelation.Triggers => "TRIGGERS",
            GraphRelation.Implements => "IMPLEMENTS",
            GraphRelation.Contains => "CONTAINS",
            GraphRelation.Inherits => "INHERITS",
            _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, null),
        };

    private static GraphRelation CypherToRelation(string relation) =>
        relation switch
        {
            "CALLS" => GraphRelation.Calls,
            "PUBLISHES" => GraphRelation.Publishes,
            "CONSUMES" => GraphRelation.Consumes,
            "HANDLES" => GraphRelation.Handles,
            "DEPENDS_ON" => GraphRelation.DependsOn,
            "USES_TOPIC" => GraphRelation.UsesTopic,
            "TRIGGERS" => GraphRelation.Triggers,
            "IMPLEMENTS" => GraphRelation.Implements,
            "CONTAINS" => GraphRelation.Contains,
            "INHERITS" => GraphRelation.Inherits,
            _ => GraphRelation.Calls,
        };
}
