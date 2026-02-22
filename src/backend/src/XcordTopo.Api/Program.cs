using System.Text.Json;
using System.Text.Json.Serialization;
using XcordTopo.Api;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Migration;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Infrastructure.Validation;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<DataOptions>(builder.Configuration.GetSection(DataOptions.SectionName));

// JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Exception handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Infrastructure services
builder.Services.AddSingleton<ITopologyStore, FileTopologyStore>();
builder.Services.AddSingleton<ITopologyValidator, TopologyValidator>();
builder.Services.AddSingleton<IHclFileManager, HclFileManager>();
builder.Services.AddSingleton<ITerraformExecutor, ProcessTerraformExecutor>();

// Providers
builder.Services.AddSingleton<IInfrastructureProvider, LinodeProvider>();
builder.Services.AddSingleton<ProviderRegistry>();

// Credentials
builder.Services.AddSingleton<ICredentialStore, FileCredentialStore>();

// Migration
builder.Services.AddSingleton<TopologyMatcher>();
builder.Services.AddSingleton<MigrationPlanGenerator>();
builder.Services.AddSingleton<IMigrationStore, FileMigrationStore>();

// Request handlers
builder.Services.AddRequestHandlers(typeof(XcordTopo.Features.Topologies.ListTopologiesHandler).Assembly);

// CORS (permissive — local tool)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure data directories exist
var dataOptions = app.Configuration.GetSection(DataOptions.SectionName).Get<DataOptions>() ?? new DataOptions();
Directory.CreateDirectory(Path.Combine(dataOptions.BasePath, "topologies"));
Directory.CreateDirectory(Path.Combine(dataOptions.BasePath, "terraform"));
Directory.CreateDirectory(Path.Combine(dataOptions.BasePath, "credentials"));
Directory.CreateDirectory(Path.Combine(dataOptions.BasePath, "migrations"));

app.UseExceptionHandler();
app.UseCors();
app.UseStaticFiles();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Map all handler endpoints from Features assembly
app.MapHandlerEndpoints(typeof(XcordTopo.Features.Topologies.ListTopologiesHandler).Assembly);

app.MapFallbackToFile("index.html");

app.Run();
