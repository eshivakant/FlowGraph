using FlowGraph.State;
using Microsoft.Extensions.Hosting;

namespace FlowGraph.Api.Startup;

public sealed class StartupInitializationHostedService(IServiceProvider services, ILogger<StartupInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Startup initialization started.");
        try
        {
            using var scope = services.CreateScope();
            var repoStore = scope.ServiceProvider.GetRequiredService<IRepoStateStore>();
            var jobStore = scope.ServiceProvider.GetRequiredService<IIndexJobStore>();

            await repoStore.InitializeAsync(cancellationToken);
            await jobStore.InitializeAsync(cancellationToken);
            logger.LogInformation("Startup initialization completed.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Startup initialization failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Startup initialization hosted service stopping.");
        return Task.CompletedTask;
    }
}
