using FlowGraph.State;
using Microsoft.Extensions.Hosting;

namespace FlowGraph.Api.Startup;

public sealed class StartupInitializationHostedService(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var repoStore = scope.ServiceProvider.GetRequiredService<IRepoStateStore>();
        var jobStore = scope.ServiceProvider.GetRequiredService<IIndexJobStore>();

        await repoStore.InitializeAsync(cancellationToken);
        await jobStore.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

