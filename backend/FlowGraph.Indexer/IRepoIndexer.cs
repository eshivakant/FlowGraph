using FlowGraph.State;

namespace FlowGraph.Indexer;

public interface IRepoIndexer
{
    Task<IndexingJob> ReindexAsync(ReindexRequest request, CancellationToken cancellationToken);
}

