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

var configuredGraphConnections = new Dictionary<string, GraphConnectionOptions>(StringComparer.OrdinalIgnoreCase);

var neo4jConnectionsSection = builder.Configuration.GetSection("FlowGraph:Neo4jConnections");
foreach (var section in neo4jConnectionsSection.GetChildren())
{
    var name = section.Key;
    var uri = section["Uri"];
    var username = section["Username"];
    var password = section["Password"];
    var database = section["Database"];

    if (!string.IsNullOrWhiteSpace(name) &&
        !string.IsNullOrWhiteSpace(uri) &&
        !string.IsNullOrWhiteSpace(username) &&
        !string.IsNullOrWhiteSpace(password))
    {
        configuredGraphConnections[name] = new GraphConnectionOptions(name, uri!, username!, password!, database);
    }
}

if (configuredGraphConnections.Count == 0)
{
    var legacyUri = builder.Configuration["FlowGraph:Neo4j:Uri"];
    var legacyUser = builder.Configuration["FlowGraph:Neo4j:Username"];
    var legacyPass = builder.Configuration["FlowGraph:Neo4j:Password"];
    var legacyDb = builder.Configuration["FlowGraph:Neo4j:Database"];
    if (!string.IsNullOrWhiteSpace(legacyUri) &&
        !string.IsNullOrWhiteSpace(legacyUser) &&
        !string.IsNullOrWhiteSpace(legacyPass))
    {
        configuredGraphConnections["Default"] = new GraphConnectionOptions("Default", legacyUri!, legacyUser!, legacyPass!, legacyDb);
    }
}

builder.Services.AddSingleton<IGraphConnectionRouter>(sp =>
{
    if (configuredGraphConnections.Count == 0)
    {
        var noop = new NoopGraphClient(sp.GetRequiredService<ILogger<NoopGraphClient>>());
        var noopMap = new Dictionary<string, IGraphWriter>(StringComparer.OrdinalIgnoreCase)
        {
            ["Default"] = noop,
        };
        return new GraphConnectionRouter(noopMap, new Dictionary<string, IGraphQueryService>(StringComparer.OrdinalIgnoreCase)
        {
            ["Default"] = noop,
        }, "Default");
    }

    var logger = sp.GetRequiredService<ILogger<Neo4jGraphClient>>();
    var writers = new Dictionary<string, IGraphWriter>(StringComparer.OrdinalIgnoreCase);
    var queries = new Dictionary<string, IGraphQueryService>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in configuredGraphConnections)
    {
        var options = entry.Value;
        var client = new Neo4jGraphClient(options.Name, options.Uri, options.Username, options.Password, options.Database, logger);
        writers[options.Name] = client;
        queries[options.Name] = client;
    }

    var defaultConnection = configuredGraphConnections.Keys.Contains("Default", StringComparer.OrdinalIgnoreCase)
        ? "Default"
        : configuredGraphConnections.Keys.First();

    return new GraphConnectionRouter(writers, queries, defaultConnection);
});

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
