using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

var cs = builder.Configuration.GetConnectionString("Default")
         ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
         ?? throw new InvalidOperationException(
             "Set ConnectionStrings:Default for HostPlatform.Worker (same PostgreSQL as API).");

builder.Services.AddDbContext<HostPlatformDbContext>(o => o.UseNpgsql(cs));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
