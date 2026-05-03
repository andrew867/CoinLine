using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HostPlatform.Infrastructure.Persistence;

public sealed class HostPlatformDbContextFactory : IDesignTimeDbContextFactory<HostPlatformDbContext>
{
    public HostPlatformDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<HostPlatformDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=hostplatform;Username=host;Password=host")
            .Options;
        return new HostPlatformDbContext(o);
    }
}
