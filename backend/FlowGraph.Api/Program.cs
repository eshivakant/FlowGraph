using FlowGraph.Blobs;
using FlowGraph.Git;
using FlowGraph.Graph;
using FlowGraph.Graph.Neo4j;
using FlowGraph.Indexer;
using FlowGraph.Roslyn;
using FlowGraph.State;
using FlowGraph.State.Sqlite;
using FlowGraph.Api.Startup;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHostedService<StartupInitializationHostedService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

static string ResolvePath(string baseDir, string path)
{
    if (Path.IsPathRooted(path))
    {
        return path;
    }
    return Path.GetFullPath(Path.Combine(baseDir, path));
}

var contentRoot = builder.Environment.ContentRootPath;
var workspaceRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));

var dataRootRaw = builder.Configuration["FlowGraph:DataRoot"] ?? ".flowgraph";
var dataRoot = ResolvePath(workspaceRoot, dataRootRaw);
Directory.CreateDirectory(dataRoot);

var logRootRaw = builder.Configuration["FlowGraph:LogRoot"] ?? Path.Combine(dataRoot, "logs");
var logRoot = ResolvePath(workspaceRoot, logRootRaw);
Directory.CreateDirectory(logRoot);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(logRoot, "server-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true);
});

var sqlitePathRaw = builder.Configuration["FlowGraph:SqlitePath"] ?? Path.Combine(dataRoot, "flowgraph.db");
var sqlitePath = ResolvePath(workspaceRoot, sqlitePathRaw);
var sqliteConnString = $"Data Source={sqlitePath}";
builder.Services.AddSingleton(sp =>
    new SqliteConnectionFactory(
        sqliteConnString,
        sp.GetRequiredService<ILogger<SqliteConnectionFactory>>()));
builder.Services.AddSingleton<IRepoStateStore, SqliteRepoStateStore>();
builder.Services.AddSingleton<IIndexJobStore, SqliteIndexJobStore>();

var storageRootRaw = builder.Configuration["FlowGraph:StorageRoot"] ?? Path.Combine(dataRoot, "storage");
var storageRoot = ResolvePath(workspaceRoot, storageRootRaw);
Directory.CreateDirectory(storageRoot);
builder.Services.AddSingleton<IBlobStore>(sp =>
    new FileSystemBlobStore(
        storageRoot,
        sp.GetRequiredService<ILogger<FileSystemBlobStore>>()));

builder.Services.AddSingleton<IGitService, GitCliService>();
builder.Services.AddSingleton<IRoslynIngestor, DeterministicRoslynIngestor>();

var neo4jUri = builder.Configuration["FlowGraph:Neo4j:Uri"];
var neo4jUser = builder.Configuration["FlowGraph:Neo4j:Username"];
var neo4jPass = builder.Configuration["FlowGraph:Neo4j:Password"];

if (!string.IsNullOrWhiteSpace(neo4jUri) && !string.IsNullOrWhiteSpace(neo4jUser) && !string.IsNullOrWhiteSpace(neo4jPass))
{
    builder.Services.AddSingleton<Neo4jGraphClient>(sp =>
        new Neo4jGraphClient(
            neo4jUri!,
            neo4jUser!,
            neo4jPass!,
            sp.GetRequiredService<ILogger<Neo4jGraphClient>>()));
    builder.Services.AddSingleton<IGraphWriter>(sp => sp.GetRequiredService<Neo4jGraphClient>());
    builder.Services.AddSingleton<IGraphQueryService>(sp => sp.GetRequiredService<Neo4jGraphClient>());
}
else
{
    builder.Services.AddSingleton<IGraphWriter, NoopGraphClient>();
    builder.Services.AddSingleton<IGraphQueryService, NoopGraphClient>();
}

var checkoutRootRaw = builder.Configuration["FlowGraph:CheckoutRoot"] ?? Path.Combine(dataRoot, "repos");
var checkoutRoot = ResolvePath(workspaceRoot, checkoutRootRaw);
Directory.CreateDirectory(checkoutRoot);
builder.Services.AddSingleton(new IndexerOptions(checkoutRoot));
builder.Services.AddSingleton<IRepoIndexer, RepoIndexer>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
// app.UseHttpsRedirection();
app.MapControllers();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
