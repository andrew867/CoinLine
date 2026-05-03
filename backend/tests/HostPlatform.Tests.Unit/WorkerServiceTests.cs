using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostPlatform.Tests.Unit;

public sealed class WorkerServiceTests
{
    [Fact]
    public void Worker_hosted_service_can_construct()
    {
        var services = new ServiceCollection();
        services.AddDbContext<HostPlatform.Infrastructure.Persistence.HostPlatformDbContext>(o =>
            o.UseInMemoryDatabase("worker-unit"));
        var sp = services.BuildServiceProvider();
        var w = new HostPlatform.Worker.Worker(sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HostPlatform.Worker.Worker>.Instance);
        Assert.NotNull(w);
    }
}
