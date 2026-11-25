using HostPlatform.Api.Options;

namespace HostPlatform.Tests.Integration;

public sealed class ApiKeyListsTests
{
    [Fact]
    public void Host_platform_keys_replace_configured_keys_and_ignore_coinline_list()
    {
        var options = new SecurityOptions { ApiKeys = ["from-config"] };

        ApiKeyLists.Apply(options, " alpha , beta ", "gamma");

        Assert.Equal(new[] { "alpha", "beta" }, options.ApiKeys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Coinline_keys_apply_when_host_platform_list_is_empty(string? hostPlatformKeys)
    {
        var options = new SecurityOptions { ApiKeys = ["from-config"] };

        ApiKeyLists.Apply(options, hostPlatformKeys, "one, two");

        Assert.Equal(new[] { "one", "two" }, options.ApiKeys);
    }

    [Fact]
    public void Empty_environment_lists_leave_configured_keys()
    {
        var options = new SecurityOptions { ApiKeys = ["from-config"] };

        ApiKeyLists.Apply(options, null, " ");

        Assert.Equal(new[] { "from-config" }, options.ApiKeys);
    }

    [Fact]
    public void Blank_host_platform_entries_do_not_fall_through_to_coinline_keys()
    {
        var options = new SecurityOptions { ApiKeys = ["from-config"] };

        ApiKeyLists.Apply(options, ",", "gamma");

        Assert.Equal(new[] { "from-config" }, options.ApiKeys);
    }
}
