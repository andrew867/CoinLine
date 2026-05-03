namespace HostPlatform.Tests.Integration;

/// <summary>Shared API factory + single in-memory database; no parallel tests (shared mutable state).</summary>
[CollectionDefinition("IntegrationApi", DisableParallelization = true)]
public sealed class IntegrationApiCollection : ICollectionFixture<ApiFixture>
{
}
