using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Channeled.Tests.Api;

public sealed class ChatWorldFixture : IAsyncLifetime
{
    public ChatWorld World { get; private set; } = null!;

    public async Task InitializeAsync() => World = await ChatWorld.CreateAsync(new AuthApiFactory());

    public async Task DisposeAsync() => await World.DisposeAsync();
}

/// <summary>Host with a tiny per-user post budget so the rate limiter is reachable in a test.</summary>
public sealed class RateLimitedChatFactory : AuthApiFactory
{
    public const int PostsPerMinute = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Chat:PostsPerMinutePerUser"] = PostsPerMinute.ToString(),
            });
        });
    }
}

public sealed class RateLimitedChatWorldFixture : IAsyncLifetime
{
    public ChatWorld World { get; private set; } = null!;

    public async Task InitializeAsync() => World = await ChatWorld.CreateAsync(new RateLimitedChatFactory());

    public async Task DisposeAsync() => await World.DisposeAsync();
}
