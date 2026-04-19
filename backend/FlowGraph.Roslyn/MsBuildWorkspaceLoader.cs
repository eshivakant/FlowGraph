using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;

namespace FlowGraph.Roslyn;

public static class MsBuildWorkspaceLoader
{
    private static int _initialized;

    public static MSBuildWorkspace CreateWorkspace()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            MSBuildLocator.RegisterDefaults();
        }

        return MSBuildWorkspace.Create();
    }
}

