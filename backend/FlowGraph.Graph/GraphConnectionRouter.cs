namespace FlowGraph.Graph;

public sealed class GraphConnectionRouter(
    IReadOnlyDictionary<string, IGraphWriter> writers,
    IReadOnlyDictionary<string, IGraphQueryService> queries,
    string defaultConnection)
    : IGraphConnectionRouter, IAsyncDisposable
{
    private readonly StringComparer _comparer = StringComparer.OrdinalIgnoreCase;
    private readonly IReadOnlyDictionary<string, IGraphWriter> _writers = writers;
    private readonly IReadOnlyDictionary<string, IGraphQueryService> _queries = queries;
    private readonly string _defaultConnection = defaultConnection;

    public IReadOnlyList<GraphConnectionInfo> ListConnections() =>
        _writers.Keys
            .OrderBy(k => k)
            .Select(k => new GraphConnectionInfo(k, _comparer.Equals(k, _defaultConnection)))
            .ToArray();

    public string ResolveConnection(string? requestedConnection)
    {
        if (!string.IsNullOrWhiteSpace(requestedConnection) && _writers.ContainsKey(requestedConnection))
        {
            return requestedConnection;
        }

        return _defaultConnection;
    }

    public IGraphWriter GetWriter(string? requestedConnection) =>
        _writers[ResolveConnection(requestedConnection)];

    public IGraphQueryService GetQueryService(string? requestedConnection) =>
        _queries[ResolveConnection(requestedConnection)];

    public async ValueTask DisposeAsync()
    {
        var disposables = _writers.Values
            .Concat(_queries.Values)
            .OfType<IAsyncDisposable>()
            .Distinct()
            .ToArray();

        foreach (var disposable in disposables)
        {
            await disposable.DisposeAsync();
        }
    }
}
