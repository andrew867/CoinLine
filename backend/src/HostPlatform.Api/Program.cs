using System.Reflection;
using System.Threading.RateLimiting;
using HostPlatform.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using HostPlatform.Api.Firmware;
using HostPlatform.Api.Health;
using HostPlatform.Api.Hosting;
using HostPlatform.Api.Middleware;
using HostPlatform.Api.Options;
using HostPlatform.Api.Services;
using HostPlatform.Api.Swagger;
using HostPlatform.Firmware;
using HostPlatform.Infrastructure.Cards;
using HostPlatform.Infrastructure.Craft;
using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PlatformOptions>(builder.Configuration.GetSection(PlatformOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(builder.Configuration.GetSection(ObservabilityOptions.SectionName));
builder.Services.Configure<JobOrchestrationOptions>(builder.Configuration.GetSection(JobOrchestrationOptions.SectionName));
builder.Services.Configure<CardPaymentOptions>(builder.Configuration.GetSection(CardPaymentOptions.SectionName));
builder.Services.Configure<FirmwareOptions>(builder.Configuration.GetSection(FirmwareOptions.SectionName));
builder.Services.Configure<DlTransportEnvironmentOptions>(builder.Configuration.GetSection(DlTransportEnvironmentOptions.SectionName));
builder.Services.PostConfigure<DlTransportEnvironmentOptions>(BindDlTransportFromEnvironment);

builder.Services.PostConfigure<SecurityOptions>(o =>
{
    var keys = builder.Configuration["HOSTPLATFORM_API_KEYS"];
    if (string.IsNullOrWhiteSpace(keys))
        return;
    var split = keys.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (split.Length > 0)
        o.ApiKeys = split;
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Logging.AddJsonConsole(o =>
    {
        o.IncludeScopes = true;
        o.TimestampFormat = "O";
    });
}

builder.Services.AddSingleton<IAuthorizationHandler, MinimumRoleAuthorizationHandler>();
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Policies.RequireOperator,
        p => p.Requirements.Add(new MinimumRoleRequirement(Policies.MinimumRole(Policies.RequireOperator))));
    o.AddPolicy(Policies.RequireTechnician,
        p => p.Requirements.Add(new MinimumRoleRequirement(Policies.MinimumRole(Policies.RequireTechnician))));
    o.AddPolicy(Policies.RequireAdmin,
        p => p.Requirements.Add(new MinimumRoleRequirement(Policies.MinimumRole(Policies.RequireAdmin))));
});

var obsOpts = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new();
if (obsOpts.MetricsEnabled)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("coinline-server"))
        .WithMetrics(m =>
            m.AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<HostPlatformDbContext>("database", tags: ["ready"])
    .AddCheck<PayloadStorageHealthCheck>("payload_storage", tags: ["ready"])
    .AddCheck<SubsystemHeartbeatHealthCheck>("worker", tags: ["ready"]);

var secOpts = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    if (secOpts.GlobalRequestsPerMinutePerIp <= 0 || builder.Environment.IsEnvironment("Testing"))
        return;

    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = secOpts.GlobalRequestsPerMinutePerIp,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

var platOptsCfg = builder.Configuration.GetSection(PlatformOptions.SectionName).Get<PlatformOptions>() ?? new();
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
    {
        if (platOptsCfg.Cors.AllowedOrigins.Length > 0)
            p.WithOrigins(platOptsCfg.Cors.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();
        else
            p.DisallowCredentials();
    });
});

if (platOptsCfg.Retention.TelemetryEnabled)
    builder.Services.AddHostedService<RetentionTelemetryHostedService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IDlXmodemTransportAdapter, DlXmodemTransportAdapter>();
builder.Services.AddScoped<IFirmwareExecutionPolicy, FirmwareExecutionPolicy>();
builder.Services.AddScoped<FirmwareJobOrchestrator>();
builder.Services.AddScoped<CapturedSessionReplayService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Host Platform API", Version = "v1" });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml))
        o.IncludeXmlComments(xml);
});
builder.Services.AddScoped<DlogTransactionEngine>();
builder.Services.AddScoped<TableDistributionService>();
builder.Services.AddSingleton<ICraftSimulationTransport, CraftSimulationTransport>();
builder.Services.AddScoped<ICardAccountLedger, CardAccountLedger>();

var cs = builder.Configuration.GetConnectionString("Default") ??
         "Host=localhost;Port=5432;Database=coinline;Username=coinline;Password=coinline";
if (builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddDbContext<HostPlatformDbContext>(o => o.UseInMemoryDatabase("test-db"));
else
    builder.Services.AddDbContext<HostPlatformDbContext>(o => o.UseNpgsql(cs));

var app = builder.Build();

ValidateProductionSecurity(app);

app.UseCors();
app.UseRateLimiter();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiKeyGateMiddleware>();
app.UseMiddleware<DevOperatorMiddleware>();
app.UseAuthorization();

var swaggerEnabled = app.Configuration.GetValue("Swagger:Enabled", true);
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

var readyChecks = new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
};
app.MapHealthChecks("/health/ready", readyChecks);
app.MapHealthChecks("/ready", readyChecks);

app.MapGet("/health/live", () => Results.Ok(new { status = "live", ts = DateTimeOffset.UtcNow }));

app.MapGet("/health", () => Results.Ok(new { status = "healthy", ts = DateTimeOffset.UtcNow }));

if (obsOpts.MetricsEnabled)
    app.MapPrometheusScrapingEndpoint(obsOpts.MetricsPath);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
    var platformOpts = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlatformOptions>>();
    if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();
    await SeedData.EnsureSeedAsync(db, platformOpts.Value.Seed.EnableDemoData);
}

app.Run();

static void ValidateProductionSecurity(WebApplication app)
{
    if (!app.Environment.IsProduction())
        return;
    var sec = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SecurityOptions>>().Value;
    if (sec.Mode == HostAuthMode.ApiKey && sec.ApiKeys.Length == 0)
    {
        throw new InvalidOperationException(
            "Production requires configured API keys when Security:Mode is ApiKey (Security:ApiKeys, COINLINE_API_KEYS, or HOSTPLATFORM_API_KEYS).");
    }
}

public partial class Program
{
    private static void BindDlTransportFromEnvironment(DlTransportEnvironmentOptions o)
    {
        o.LiveDlaEnabled = ReadEnvBool("COINLINE_FIRMWARE_LIVE_DLA_ENABLED", o.LiveDlaEnabled);
        var transport = Environment.GetEnvironmentVariable("COINLINE_DLA_TRANSPORT");
        if (!string.IsNullOrWhiteSpace(transport))
            o.TransportKind = transport.Trim();
        var serialPort = Environment.GetEnvironmentVariable("COINLINE_DLA_SERIAL_PORT");
        if (!string.IsNullOrWhiteSpace(serialPort))
            o.SerialPort = serialPort.Trim();
        if (int.TryParse(Environment.GetEnvironmentVariable("COINLINE_DLA_BAUD"), out var baud) && baud > 0)
            o.Baud = baud;
        if (int.TryParse(Environment.GetEnvironmentVariable("COINLINE_DLA_TIMEOUT_MS"), out var to) && to > 0)
            o.TimeoutMs = to;
        if (int.TryParse(Environment.GetEnvironmentVariable("COINLINE_DLA_MAX_RETRIES"), out var mr) && mr >= 0)
            o.MaxRetries = mr;
        if (int.TryParse(Environment.GetEnvironmentVariable("COINLINE_DLA_PACING_MS"), out var p) && p >= 0)
            o.PacingMs = p;
        var tcpHost = Environment.GetEnvironmentVariable("COINLINE_DLA_TCP_HOST");
        if (!string.IsNullOrWhiteSpace(tcpHost))
            o.TcpHost = tcpHost.Trim();
        if (int.TryParse(Environment.GetEnvironmentVariable("COINLINE_DLA_TCP_PORT"), out var tp) && tp > 0)
            o.TcpPort = tp;
        var pipe = Environment.GetEnvironmentVariable("COINLINE_DLA_PIPE");
        if (!string.IsNullOrWhiteSpace(pipe))
            o.PipeName = pipe.Trim();
    }

    private static bool ReadEnvBool(string name, bool fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v))
            return fallback;
        return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
    }
}
