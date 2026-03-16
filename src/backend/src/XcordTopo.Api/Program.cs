using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using XcordTopo.Api;
using XcordTopo.Infrastructure.Credentials;
using XcordTopo.Infrastructure.Migration;
using XcordTopo.Infrastructure.Plugins;
using XcordTopo.Infrastructure.Plugins.Images;
using XcordTopo.Infrastructure.Providers;
using XcordTopo.Infrastructure.Storage;
using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Infrastructure.Validation;
using XcordTopo.PluginSdk;

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
builder.Services.AddSingleton<IImagePushExecutor, ProcessImagePushExecutor>();

// Image plugins (built-in)
builder.Services.AddSingleton<IImagePlugin, PostgreSqlImagePlugin>();
builder.Services.AddSingleton<IImagePlugin, RedisImagePlugin>();
builder.Services.AddSingleton<IImagePlugin, MinIOImagePlugin>();
builder.Services.AddSingleton<IImagePlugin, LiveKitImagePlugin>();
builder.Services.AddSingleton<IImagePlugin, HubServerImagePlugin>();
builder.Services.AddSingleton<IImagePlugin, FederationServerImagePlugin>();
builder.Services.AddSingleton<IImagePlugin, RegistryImagePlugin>();
builder.Services.AddSingleton<IImagePlugin, CustomImagePlugin>();

// External plugins (loaded from plugins directory)
var earlyDataOptions = builder.Configuration.GetSection(DataOptions.SectionName).Get<DataOptions>() ?? new DataOptions();
var pluginDir = Path.Combine(earlyDataOptions.BasePath, "plugins");
if (Directory.Exists(pluginDir))
{
    foreach (var dll in Directory.GetFiles(pluginDir, "*.dll"))
    {
        try
        {
            var context = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(dll), isCollectible: false);
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));
            foreach (var type in assembly.GetExportedTypes()
                .Where(t => typeof(IImagePlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                builder.Services.AddSingleton(typeof(IImagePlugin), type);
                Console.WriteLine($"[Plugins] Registered external plugin: {type.Name} from {Path.GetFileName(dll)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Plugins] Failed to load {Path.GetFileName(dll)}: {ex.Message}");
        }
    }
}

// Plugin registry (collects all IImagePlugin registrations)
builder.Services.AddSingleton<ImagePluginRegistry>();

// Providers
builder.Services.AddSingleton<LinodeProvider>();
builder.Services.AddSingleton<AwsProvider>();
builder.Services.AddSingleton<ICloudProvider>(sp => sp.GetRequiredService<LinodeProvider>());
builder.Services.AddSingleton<ICloudProvider>(sp => sp.GetRequiredService<AwsProvider>());
builder.Services.AddSingleton<ProviderRegistry>();
builder.Services.AddSingleton<MultiProviderHclGenerator>();

// Credentials
builder.Services.AddSingleton<ICredentialStore, FileCredentialStore>();

// Migration
builder.Services.AddSingleton<TopologyMatcher>();
builder.Services.AddSingleton<MigrationPlanGenerator>();
builder.Services.AddSingleton<IMigrationStore, FileMigrationStore>();

// Request handlers
builder.Services.AddRequestHandlers(typeof(XcordTopo.Features.Topologies.ListTopologiesHandler).Assembly);

// CORS (permissive - local tool)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure base data directory exists (per-topology subdirectories are created on demand)
var dataOptions = app.Configuration.GetSection(DataOptions.SectionName).Get<DataOptions>() ?? new DataOptions();
Directory.CreateDirectory(dataOptions.BasePath);

app.UseExceptionHandler();
app.UseCors();
app.UseStaticFiles();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Map all handler endpoints from Features assembly
app.MapHandlerEndpoints(typeof(XcordTopo.Features.Topologies.ListTopologiesHandler).Assembly);

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
